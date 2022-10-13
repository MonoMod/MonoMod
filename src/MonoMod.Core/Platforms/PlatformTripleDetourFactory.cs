using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MonoMod.Core.Platforms {

    public class PlatformTripleDetourFactory : IDetourFactory {

        private readonly PlatformTriple triple;

        public PlatformTripleDetourFactory(PlatformTriple triple) {
            this.triple = triple;
        }

        public FeatureFlags SupportedFeatures => triple.SupportedFeatures;

        public ICoreDetour CreateDetour(CreateDetourRequest request) {
            Helpers.ThrowIfArgumentNull(request.Source);
            Helpers.ThrowIfArgumentNull(request.Target);

            if (!triple.TryDisableInlining(request.Source))
                MMDbgLog.Log($"Could not disable inlining of method {request.Source.GetID()}; detours may not be reliable");

            var detour = new Detour(triple, request.Source, request.Target);
            if (request.ApplyByDefault) {
                detour.Apply();
            }
            return detour;
        }

        private sealed class Detour : ICoreDetour {
            private readonly PlatformTriple triple;
            private readonly MethodBase realTarget;

            private sealed class DetourBox {
                private bool applyDetours;
                public bool IsApplied {
                    get => Volatile.Read(ref applyDetours);
                    set {
                        Volatile.Write(ref applyDetours, value);
                        Thread.MemoryBarrier();
                    }
                }

                private bool isApplying;
                public bool IsApplying {
                    get => Volatile.Read(ref isApplying);
                    set {
                        Volatile.Write(ref isApplying, value);
                        Thread.MemoryBarrier();
                    }
                }

                public SimpleNativeDetour? Detour;
                
                private readonly PlatformTriple triple;
                private readonly MethodBase src;
                private readonly MethodBase target;

                public DetourBox(PlatformTriple triple, MethodBase src, MethodBase target) {
                    this.triple = triple;
                    this.src = src;
                    this.target = target;
                    applyDetours = false;
                    isApplying = false;
                    Detour = null;
                }

                public void SubscribeCompileMethod() {
                    AddRelatedDetour(src, this);
                    AddRelatedDetour(target, this);
                }

                public void UnsubscribeCompileMethod() {
                    RemoveRelatedDetour(src, this);
                    RemoveRelatedDetour(target, this);
                }

                // TODO: figure out the goddamn locking here, what is even going on right now?
                public void OnMethodCompiled(MethodBase method, IntPtr codeStart, ulong codeSize) {
                    if (!IsApplied)
                        return;

                    method = triple.GetIdentifiable(method);
                    var isFrom = method.Equals(src);
                    var isTo = method.Equals(target);
                    if (!isFrom && !isTo)
                        return;
                    Helpers.DAssert(!(isFrom && isTo));

                    SimpleNativeDetour? oldDetour;
                    lock (this) {
                        if (!IsApplied)
                            return;
                        if (IsApplying)
                            return;

                        try {
                            IsApplying = true;

                            IntPtr from, to;

                            var detour = Detour;

                            if (detour is not null) {
                                (from, to) = (detour.Source, detour.Destination);
                                if (isFrom) {
                                    from = codeStart;
                                } else {
                                    to = codeStart;
                                    // we already have a detour, and are just changing the target, retarget
                                    detour.ChangeTarget(to);
                                    return;
                                }
                            } else {
                                if (isFrom) {
                                    from = codeStart;
                                    to = triple.GetNativeMethodBody(target);
                                } else {
                                    from = triple.GetNativeMethodBody(src);
                                    to = codeStart;
                                }
                            }

                            var newDetour = triple.CreateSimpleDetour(from, to, detourMaxSize: (int) codeSize);
                            ReplaceDetourInLock(this, newDetour, out oldDetour);
                        } finally {
                            IsApplying = false;
                        }
                    }

                }
            }

            public Detour(PlatformTriple triple, MethodBase src, MethodBase dst) {
                this.triple = triple;
                Source = src;
                Target = dst;

                realTarget = triple.GetRealDetourTarget(src, dst);

                detourBox = new(triple, Source, realTarget);

                if (triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook)) {
                    EnsureSubscribed(triple);
                    detourBox.SubscribeCompileMethod();
                }
            }

            private static readonly object subLock = new();
            private static bool hasSubscribed = false;

            // TODO: this currently assumes a singleton PlatformTriple. That isn't necessarily *always* the case, though it should be.
            private static void EnsureSubscribed(PlatformTriple triple) {
                if (Volatile.Read(ref hasSubscribed))
                    return;
                lock (subLock) {
                    if (Volatile.Read(ref hasSubscribed))
                        return;
                    Volatile.Write(ref hasSubscribed, true);

                    triple.Runtime.OnMethodCompiled += OnMethodCompiled;
                }
            }

            private class RelatedTargetObject {
                public readonly List<DetourBox> RelatedDetours = new();
            }

            private static readonly ConditionalWeakTable<MethodBase, RelatedTargetObject> relatedDetours = new();
            private static void AddRelatedDetour(MethodBase m, DetourBox cmh) {
                var related = relatedDetours.GetOrCreateValue(m);
                lock (related) {
                    related.RelatedDetours.Add(cmh);
                    if (related.RelatedDetours.Count > 2) {
                        MMDbgLog.Log($"WARNING: More than 2 related detours for method {m}! This means that the method has been detoured twice. Detour cleanup will fail.");
                    }
                }
            }

            private static void RemoveRelatedDetour(MethodBase m, DetourBox cmh) {
                var related = relatedDetours.GetOrCreateValue(m);
                lock (related) {
                    related.RelatedDetours.Remove(cmh);
                }
            }

            private static void OnMethodCompiled(MethodBase? method, IntPtr codeStart, ulong codeSize) {
                if (method is null) {
                    return;
                }

                method = PlatformTriple.Current.GetIdentifiable(method);

                if (relatedDetours.TryGetValue(method, out var related)) {
                    lock (related) {
                        foreach (var cmh in related.RelatedDetours) {
                            cmh.OnMethodCompiled(method, codeStart, codeSize);
                        }
                    }
                }
            }

            private static void ReplaceDetourInLock(DetourBox nativeDetour, SimpleNativeDetour? newDetour, out SimpleNativeDetour? oldDetour) {
                Thread.MemoryBarrier();
                oldDetour = Interlocked.Exchange(ref nativeDetour.Detour, newDetour);
            }

            public MethodBase Source { get; }

            public MethodBase Target { get; }

            // These fields are disposed if needed, just through some more indirections than is typical.
#pragma warning disable CA2213 // Disposable fields should be disposed
            private IDisposable? srcPin;
            private IDisposable? dstPin;
#pragma warning restore CA2213 // Disposable fields should be disposed
            private readonly DetourBox detourBox;

            public bool IsApplied => detourBox.IsApplied;

            public void Apply() {
                lock (detourBox) {
                    if (IsApplied)
                        throw new InvalidOperationException("Cannot apply a detour which is already applied");

                    try {
                        detourBox.IsApplying = true;
                        detourBox.IsApplied = true;

                        srcPin = triple.PinMethodIfNeeded(Source);
                        dstPin = triple.PinMethodIfNeeded(realTarget);

                        var from = triple.GetNativeMethodBody(Source);
                        var to = triple.GetNativeMethodBody(realTarget);

                        ReplaceDetourInLock(detourBox, triple.CreateSimpleDetour(from, to), out SimpleNativeDetour? oldDetour);
                        Helpers.DAssert(oldDetour is null);
                    } catch {
                        detourBox.IsApplied = false;
                        throw;
                    } finally {
                        detourBox.IsApplying = false;
                    }
                }
            }

            public void Undo() {
                lock (detourBox) {
                    if (!IsApplied)
                        throw new InvalidOperationException("Cannot undo a detour which is not applied");
                    try {
                        detourBox.IsApplying = true;
                        UndoCore(out SimpleNativeDetour? oldDetour);
                        // we want to do this in-lock to make sure that it gets cleaned up properly
                        oldDetour?.Dispose();
                    } finally {
                        detourBox.IsApplying = false;
                    }
                }
            }

            private void UndoCore(out SimpleNativeDetour? oldDetour) {
                detourBox.IsApplied = false;
                ReplaceDetourInLock(detourBox, null, out oldDetour);
                Interlocked.Exchange(ref srcPin, null)?.Dispose();
                Interlocked.Exchange(ref dstPin, null)?.Dispose();
            }

            #region IDisposable implementation
            private bool disposedValue;

            private void Dispose(bool disposing) {
                if (!disposedValue) {
                    if (triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook)) {
                        detourBox.UnsubscribeCompileMethod();
                    }
                    SimpleNativeDetour? oldDetour;
                    lock (detourBox) {
                        UndoCore(out oldDetour);
                        oldDetour?.Dispose();
                    }

                    disposedValue = true;
                }
            }

            ~Detour() {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: false);
            }

            public void Dispose() {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}
