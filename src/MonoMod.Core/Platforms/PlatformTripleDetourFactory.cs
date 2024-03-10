using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// An <see cref="IDetourFactory"/> implementation based on <see cref="PlatformTriple"/>.
    /// </summary>
    internal sealed class PlatformTripleDetourFactory : IDetourFactory
    {

        private readonly PlatformTriple triple;

        /// <summary>
        /// Constructs a <see cref="PlatformTripleDetourFactory"/> based on the provided <see cref="PlatformTriple"/>.
        /// </summary>
        /// <param name="triple">The <see cref="PlatformTriple"/> to create a detour factory using.</param>
        public PlatformTripleDetourFactory(PlatformTriple triple)
        {
            this.triple = triple;
        }

        /// <inheritdoc/>
        public ICoreDetour CreateDetour(CreateDetourRequest request)
        {
            Helpers.ThrowIfArgumentNull(request.Source);
            Helpers.ThrowIfArgumentNull(request.Target);

            if (!triple.TryDisableInlining(request.Source))
                MMDbgLog.Warning($"Could not disable inlining of method {request.Source}; detours may not be reliable");

            var detour = new Detour(triple, request.Source, request.Target);
            if (request.ApplyByDefault)
            {
                detour.Apply();
            }
            return detour;
        }

        private abstract class DetourBase : ICoreDetourBase
        {
            protected readonly PlatformTriple Triple;

            protected DetourBase(PlatformTriple triple)
            {
                Triple = triple;
                DetourBox = null!;
            }

            protected abstract class DetourBoxBase
            {
                public bool IsApplied
                {
                    get => Volatile.Read(ref applyDetours);
                    set
                    {
                        Volatile.Write(ref applyDetours, value);
                        Thread.MemoryBarrier();
                    }
                }

                public bool IsApplying
                {
                    get => Volatile.Read(ref isApplying);
                    set
                    {
                        Volatile.Write(ref isApplying, value);
                        Thread.MemoryBarrier();
                    }
                }

                public SimpleNativeDetour? Detour;

                protected readonly PlatformTriple Triple;
                protected readonly object Sync = new();
                private bool applyDetours;
                private bool isApplying;


                protected DetourBoxBase(PlatformTriple triple)
                {
                    Triple = triple;
                    applyDetours = false;
                    isApplying = false;
                }
            }

            protected DetourBoxBase DetourBox;
            protected TBox GetDetourBox<TBox>() where TBox : DetourBoxBase => Unsafe.As<TBox>(DetourBox);

            public bool IsApplied => DetourBox.IsApplied;

            // TODO: instead of letting go of the old detour, keep it around, because it seems like the runtime doesn't free old code versions
            protected static void ReplaceDetourInLock(DetourBoxBase nativeDetour, SimpleNativeDetour? newDetour, out SimpleNativeDetour? oldDetour)
            {
                Thread.MemoryBarrier();
                oldDetour = Interlocked.Exchange(ref nativeDetour.Detour, newDetour);
            }

            protected abstract SimpleNativeDetour CreateDetour();

            public void Apply()
            {
                lock (DetourBox)
                {
                    if (IsApplied)
                        throw new InvalidOperationException("Cannot apply a detour which is already applied");

                    try
                    {
                        DetourBox.IsApplying = true;
                        DetourBox.IsApplied = true;

                        ReplaceDetourInLock(DetourBox, CreateDetour(), out var oldDetour);
                        Helpers.DAssert(oldDetour is null);
                    }
                    catch
                    {
                        DetourBox.IsApplied = false;
                        throw;
                    }
                    finally
                    {
                        DetourBox.IsApplying = false;
                    }
                }
            }

            protected abstract void BeforeUndo();
            protected abstract void AfterUndo();

            public void Undo()
            {
                lock (DetourBox)
                {
                    if (!IsApplied)
                        throw new InvalidOperationException("Cannot undo a detour which is not applied");
                    try
                    {
                        DetourBox.IsApplying = true;

                        UndoCore(out var oldDetour);
                        // we want to do this in-lock to make sure that it gets cleaned up properly
                        oldDetour?.Dispose();
                    }
                    finally
                    {
                        DetourBox.IsApplying = false;
                    }
                }
            }

            private void UndoCore(out SimpleNativeDetour? oldDetour)
            {
                BeforeUndo();
                DetourBox.IsApplied = false;
                ReplaceDetourInLock(DetourBox, null, out oldDetour);
                AfterUndo();
            }

            protected abstract void BeforeDispose();

            #region IDisposable implementation
            private bool disposedValue;

            private void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    BeforeDispose();

                    lock (DetourBox)
                    {
                        UndoCore(out var oldDetour);
                        oldDetour?.Dispose();
                    }

                    disposedValue = true;
                }
            }

            ~DetourBase()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }

        private sealed class Detour : DetourBase, ICoreDetour
        {
            private readonly MethodBase realTarget;

            private sealed class ManagedDetourBox : DetourBoxBase
            {
                private readonly MethodBase src;
                private readonly MethodBase target;

                public ManagedDetourBox(PlatformTriple triple, MethodBase src, MethodBase target) : base(triple)
                {
                    this.src = src;
                    this.target = target;
                    Detour = null;
                }

                public void SubscribeCompileMethod()
                {
                    AddRelatedDetour(src, this);
                    //AddRelatedDetour(target, this);
                }

                public void UnsubscribeCompileMethod()
                {
                    RemoveRelatedDetour(src, this);
                    //RemoveRelatedDetour(target, this);
                }

                // TODO: figure out the goddamn locking here, what is even going on right now?
                public void OnMethodCompiled(MethodBase method, IntPtr codeStart, IntPtr codeStartRw, ulong codeSize)
                {
                    if (!IsApplied)
                        return;

                    method = Triple.GetIdentifiable(method);
                    Helpers.DAssert(method.Equals(src));

                    lock (Sync)
                    {
                        if (!IsApplied)
                            return;
                        if (IsApplying)
                            return;

                        MMDbgLog.Trace($"Updating detour from {src} to {target} (recompiled {method} to {codeStart:x16})");

                        try
                        {
                            IsApplying = true;

                            IntPtr from, to, fromRw;

                            var detour = Detour;

                            if (detour is not null)
                            {
                                (_, to) = (detour.Source, detour.Destination);

                                from = codeStart;
                                fromRw = codeStartRw;
                            }
                            else
                            {
                                from = codeStart;
                                fromRw = codeStartRw;
                                to = Triple.Runtime.GetMethodHandle(target).GetFunctionPointer();
                            }

                            var newDetour = Triple.CreateSimpleDetour(from, to, detourMaxSize: (int)codeSize, fromRw: fromRw);
                            ReplaceDetourInLock(this, newDetour, out _);
                        }
                        finally
                        {
                            IsApplying = false;
                        }
                    }

                }
            }

            private new ManagedDetourBox DetourBox => GetDetourBox<ManagedDetourBox>();

            public Detour(PlatformTriple triple, MethodBase src, MethodBase dst) : base(triple)
            {
                Source = triple.GetIdentifiable(src);
                Target = dst;

                realTarget = triple.GetRealDetourTarget(src, dst);

                base.DetourBox = new ManagedDetourBox(triple, Source, realTarget);

                if (triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook))
                {
                    EnsureSubscribed(triple);
                    DetourBox.SubscribeCompileMethod();
                }
            }

            private static readonly object subLock = new();
            private static bool hasSubscribed;

            // TODO: this currently assumes a singleton PlatformTriple. That isn't necessarily *always* the case, though it should be.
            private static void EnsureSubscribed(PlatformTriple triple)
            {
                if (Volatile.Read(ref hasSubscribed))
                    return;
                lock (subLock)
                {
                    if (Volatile.Read(ref hasSubscribed))
                        return;
                    Volatile.Write(ref hasSubscribed, true);

                    triple.Runtime.OnMethodCompiled += OnMethodCompiled;
                }
            }

            private sealed class RelatedDetourBag
            {
                public readonly MethodBase Method;
                public readonly List<ManagedDetourBox> RelatedDetours = new();
                public bool IsValid = true;

                public RelatedDetourBag(MethodBase method)
                    => Method = method;
            }

            private static readonly ConcurrentDictionary<MethodBase, RelatedDetourBag> relatedDetours = new();
            private static void AddRelatedDetour(MethodBase m, ManagedDetourBox cmh)
            {
                Retry:
                var related = relatedDetours.GetOrAdd(m, static m => new(m));
                lock (related)
                {
                    if (!related.IsValid)
                        goto Retry;
                    related.RelatedDetours.Add(cmh);
                    if (related.RelatedDetours.Count > 1)
                    {
                        MMDbgLog.Warning($"Multiple related detours for method {m}! This means that the method has been detoured twice. Detour cleanup will fail.");
                    }
                }
            }

            private static void RemoveRelatedDetour(MethodBase m, ManagedDetourBox cmh)
            {
                if (relatedDetours.TryGetValue(m, out var related))
                {
                    lock (related)
                    {
                        related.RelatedDetours.Remove(cmh);
                        // if we have no related detours for the method, we want to remove it from the dictionary to not keep the method alive if its in a collectible ALC
                        if (related.RelatedDetours.Count == 0)
                        {
                            related.IsValid = false;
                            Helpers.Assert(relatedDetours.TryRemove(related.Method, out _));
                        }
                    }
                }
                else
                {
                    MMDbgLog.Warning($"Attempted to remove a related detour from method {m} which has no RelatedDetourBag");
                }
            }

            private static void OnMethodCompiled(RuntimeMethodHandle methodHandle, MethodBase? method, IntPtr codeStart, IntPtr codeStartRw, ulong codeSize)
            {
                if (method is null)
                {
                    return;
                }

                method = PlatformTriple.Current.GetIdentifiable(method);

                if (relatedDetours.TryGetValue(method, out var related))
                {
                    lock (related)
                    {
                        foreach (var cmh in related.RelatedDetours)
                        {
                            cmh.OnMethodCompiled(method, codeStart, codeStartRw, codeSize);
                        }
                    }
                }
            }

            public MethodBase Source { get; }

            public MethodBase Target { get; }

            // These fields are disposed if needed, just through some more indirections than is typical.
#pragma warning disable CA2213 // Disposable fields should be disposed
            private IDisposable? srcPin;
            private IDisposable? dstPin;
#pragma warning restore CA2213 // Disposable fields should be disposed

            protected override SimpleNativeDetour CreateDetour()
            {
                MMDbgLog.Trace($"Applying managed detour from {Source} to {realTarget}");

                srcPin = Triple.PinMethodIfNeeded(Source);
                dstPin = Triple.PinMethodIfNeeded(realTarget);

                Triple.Compile(Source);
                var from = Triple.GetNativeMethodBody(Source);
                Triple.Compile(realTarget);
                var to = Triple.Runtime.GetMethodHandle(realTarget).GetFunctionPointer();

                return Triple.CreateSimpleDetour(from, to);
            }

            protected override void BeforeUndo()
            {
                MMDbgLog.Trace($"Undoing managed detour from {Source} to {realTarget}");
            }

            protected override void AfterUndo()
            {
                Interlocked.Exchange(ref srcPin, null)?.Dispose();
                Interlocked.Exchange(ref dstPin, null)?.Dispose();
            }

            protected override void BeforeDispose()
            {
                if (Triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook))
                {
                    DetourBox.UnsubscribeCompileMethod();
                }
            }
        }

        private sealed class NativeDetour : DetourBase, ICoreNativeDetour
        {
            public IntPtr Source => DetourBox.From;

            public IntPtr Target => DetourBox.To;

            public bool HasOrigEntrypoint => OrigEntrypoint != IntPtr.Zero;

            public IntPtr OrigEntrypoint { get; private set; }
            private IDisposable? origHandle;

            private sealed class NativeDetourBox : DetourBoxBase
            {
                public readonly IntPtr From;
                public readonly IntPtr To;

                public NativeDetourBox(PlatformTriple triple, IntPtr from, IntPtr to) : base(triple)
                {
                    From = from;
                    To = to;
                }
            }

            private new NativeDetourBox DetourBox => GetDetourBox<NativeDetourBox>();

            public NativeDetour(PlatformTriple triple, IntPtr from, IntPtr to) : base(triple)
            {
                base.DetourBox = new NativeDetourBox(triple, from, to);
            }

            // NOTE: When we eventually add retargeting, we'll need to change the origEntrypoint. We'll want to tie the lifetime 
            // of origHandle to any delegates that use origEntrypoint, and so may need to expose it publicly. Alternately, we can
            // have only 

            protected override SimpleNativeDetour CreateDetour()
            {
                MMDbgLog.Trace($"Applying native detour from {Source:x16} to {Target:x16}");

                var (simple, altEntry, altHandle) = Triple.CreateNativeDetour(Source, Target);
                (altHandle, origHandle) = (origHandle, altHandle);
                Helpers.DAssert(altHandle is null);
                OrigEntrypoint = altEntry;
                return simple;
            }

            protected override void BeforeUndo()
            {
                MMDbgLog.Trace($"Undoing native detour from {Source:x16} to {Target:x16}");
            }

            protected override void AfterUndo()
            {
                OrigEntrypoint = IntPtr.Zero;
                origHandle?.Dispose();
                origHandle = null;
            }

            protected override void BeforeDispose() { }
        }

        /// <inheritdoc/>
        public ICoreNativeDetour CreateNativeDetour(CreateNativeDetourRequest request)
        {
            var detour = new NativeDetour(triple, request.Source, request.Target);
            if (request.ApplyByDefault)
            {
                detour.Apply();
            }
            return detour;
        }
    }
}
