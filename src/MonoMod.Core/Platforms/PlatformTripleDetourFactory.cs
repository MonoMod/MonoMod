using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
            private readonly CompileMethodHook? compileMethodHook;

            private sealed class DetourBox {
                public bool IsDetourReleased;

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

                public NativeDetour? Detour;
            }

            // TODO: don't have a OnMethodCompiled subscription for each Detour
            public Detour(PlatformTriple triple, MethodBase src, MethodBase dst) {
                this.triple = triple;
                Source = src;
                Target = dst;
                nativeDetour = new();

                realTarget = triple.GetRealDetourTarget(src, dst);

                if (triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook)) {
                    compileMethodHook = new CompileMethodHook(triple, src, realTarget, nativeDetour);
                    triple.Runtime.OnMethodCompiled += compileMethodHook.OnMethodCompiled;
                }
            }

            private sealed class CompileMethodHook {
                private readonly PlatformTriple triple;
                private readonly MethodBase src;
                private readonly MethodBase target;
                private readonly DetourBox nativeDetour;

                public CompileMethodHook(PlatformTriple triple, MethodBase src, MethodBase target, DetourBox nativeDetour) {
                    this.triple = triple;
                    this.src = src;
                    this.target = target;
                    this.nativeDetour = nativeDetour;
                }

                public void OnMethodCompiled(MethodBase? method, IntPtr codeStart, ulong codeSize) {
                    if (method is null)
                        return;

                    if (!nativeDetour.IsApplied)
                        return;

                    method = triple.GetIdentifiable(method);
                    var isFrom = method.Equals(src);
                    var isTo = method.Equals(target);
                    if (!isFrom && !isTo)
                        return;
                    Helpers.DAssert(!(isFrom && isTo));

                    NativeDetour? oldDetour;
                    lock (nativeDetour) {
                        if (nativeDetour.IsApplying)
                            return;

                        try {
                            nativeDetour.IsApplying = true;

                            IntPtr from, to;

                            var detour = nativeDetour.Detour;

                            if (detour is not null) {
                                (from, to) = (detour.Source, detour.Destination);
                                if (isFrom) {
                                    from = codeStart;
                                } else {
                                    to = codeStart;
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

                            var newDetour = triple.CreateNativeDetour(from, to, detourMaxSize: (int) codeSize);
                            ReplaceDetourInLock(nativeDetour, newDetour, out oldDetour);
                        } finally {
                            nativeDetour.IsApplying = false;
                        }
                    }

                    ReplaceDetourOutOfLock(oldDetour);
                }
            }

            private static void ReplaceDetourInLock(DetourBox nativeDetour, NativeDetour? newDetour, out NativeDetour? oldDetour) {
                Thread.MemoryBarrier();
                oldDetour = Interlocked.Exchange(ref nativeDetour.Detour, newDetour);
                if (Volatile.Read(ref nativeDetour.IsDetourReleased)) {
                    newDetour?.MakeManualOnly();
                }
            }

            private static void ReplaceDetourOutOfLock(NativeDetour? oldDetour) {
                // we want to make sure our old detour is set to Automatic, then just release it to be cleaned up on the next GC, which
                // we'll presume is *after* the new method body is in place
                if (oldDetour is { } old && !old.IsAutoUndone) {
                    old.MakeAutomatic();
                }
            }

            public MethodBase Source { get; }

            public MethodBase Target { get; }

            // These fields are disposed if needed, just through some more indirections than is typical.
#pragma warning disable CA2213 // Disposable fields should be disposed
            private IDisposable? srcPin;
            private IDisposable? dstPin;
#pragma warning restore CA2213 // Disposable fields should be disposed
            private readonly DetourBox nativeDetour;

            public bool IsApplied => nativeDetour.IsApplied;

            public bool IsAttached { get; private set; } = true;

            public void Apply() {
                lock (nativeDetour) {
                    if (IsApplied)
                        throw new InvalidOperationException("Cannot apply a detour which is already applied");

                    try {
                        nativeDetour.IsApplying = true;
                        nativeDetour.IsApplied = true;

                        srcPin = triple.PinMethodIfNeeded(Source);
                        dstPin = triple.PinMethodIfNeeded(realTarget);

                        var from = triple.GetNativeMethodBody(Source);
                        var to = triple.GetNativeMethodBody(realTarget);

                        ReplaceDetourInLock(nativeDetour, triple.CreateNativeDetour(from, to), out NativeDetour? oldDetour);
                        Helpers.DAssert(oldDetour is null);
                    } catch {
                        nativeDetour.IsApplied = false;
                        throw;
                    } finally {
                        nativeDetour.IsApplying = false;
                    }
                }
            }

            public void Undo() {
                lock (nativeDetour) {
                    if (!IsApplied)
                        throw new InvalidOperationException("Cannot undo a detour which is not applied");
                    try {
                        nativeDetour.IsApplying = true;
                        UndoCore(out NativeDetour? oldDetour);
                        // we want to do this in-lock to make sure that it gets cleaned up properly
                        ReplaceDetourOutOfLock(oldDetour);
                        oldDetour?.Dispose();
                    } finally {
                        nativeDetour.IsApplying = false;
                    }
                }
            }

            private void UndoCore(out NativeDetour? oldDetour) {
                nativeDetour.IsApplied = false;
                Interlocked.Exchange(ref srcPin, null)?.Dispose();
                Interlocked.Exchange(ref dstPin, null)?.Dispose();
                ReplaceDetourInLock(nativeDetour, null, out oldDetour);
            }

            public void Attach() {
                lock (nativeDetour) {
                    if (IsAttached)
                        throw new InvalidOperationException("Cannot attach an already attached detour");
                    nativeDetour.Detour?.MakeAutomatic();
                    IsAttached = true;

                    Volatile.Write(ref nativeDetour.IsDetourReleased, false);
                    Thread.MemoryBarrier();
                }
            }

            public void Detatch() {
                lock (nativeDetour) {
                    if (!IsAttached)
                        throw new InvalidOperationException("Cannot detatch an already detached detour");
                    nativeDetour.Detour?.MakeManualOnly();

                    Volatile.Write(ref nativeDetour.IsDetourReleased, true);
                    Thread.MemoryBarrier();
                }
            }

            #region IDisposable implementation
            private bool disposedValue;

            private void Dispose(bool disposing) {
                if (!disposedValue) {
                    if (IsAttached) {
                        // we're attached, we need to undo the detour if its applied
                        if (compileMethodHook is not null) {
                            // if this detour applied a callback, remove it
                            triple.Runtime.OnMethodCompiled -= compileMethodHook.OnMethodCompiled;
                        }
                        UndoCore(out var oldDetour);
                        ReplaceDetourOutOfLock(oldDetour);
                        oldDetour?.Dispose();
                    } else {
                        // TODO: move these GCHandle allocs to the point where IsAttached is set to false

                        // otherwise, we need to ensure the pins live forever, and make the native detour ManualOnly and dispose it
                        _ = GCHandle.Alloc(Interlocked.Exchange(ref srcPin, null));
                        _ = GCHandle.Alloc(Interlocked.Exchange(ref dstPin, null));

                        // we only want to dispose nativeDetour if we don't have a compileMethodHook
                        // if we *do* have a compileMethodHook, then it should stick around to ensure that
                        // the detour remains properly updated
                        if (compileMethodHook is null) {
                            var detour = Interlocked.Exchange(ref nativeDetour.Detour, null);
                            if (detour is not null && disposing) {
                                detour.Dispose();
                            }
                        }
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
