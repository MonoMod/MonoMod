using Mono.Cecil.Cil;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

#if NET9_0_OR_GREATER
using Lock = System.Threading.Lock;
#else
using Lock = object;
#endif


namespace MonoMod.Core.Platforms
{
    internal sealed class PlatformTripleDetourFactory : IDetourFactory
    {
        private readonly PlatformTriple triple;

        public PlatformTripleDetourFactory(PlatformTriple triple)
            => this.triple = triple;

        public bool SupportsNativeDetourOrigEntrypoint => triple.SupportedFeatures.Has(ArchitectureFeature.CreateAltEntryPoint);


        private bool SupportsJitHook => triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook);

        private readonly Lock jitHookLock = new();
        private bool jitHookAttached;

        private void EnsureJitHook()
        {
            if (Volatile.Read(ref jitHookAttached))
            {
                return;
            }

            lock (jitHookLock)
            {
                if (Volatile.Read(ref jitHookAttached))
                {
                    return;
                }

                triple.Runtime.OnMethodCompiled += OnMethodCompiled;
                Volatile.Write(ref jitHookAttached, true);
            }
        }

        private void OnMethodCompiled(RuntimeMethodHandle methodHandle, MethodBase? method, IntPtr codeStart, IntPtr codeRw, ulong codeSize)
        {
            if (method is null)
            {
                return;
            }

            method = triple.GetIdentifiable(method);

            if (relatedDetours.TryGetValue(method, out var related))
            {
                lock (related.Lock)
                {
                    foreach (var box in related.RelatedDetours)
                    {
                        box.OnRelatedMethodCompiled(method, methodHandle, codeStart, codeRw, codeSize);
                    }
                }
            }
        }

        private sealed class RelatedDetourBag
        {
            public readonly Lock Lock = new();
            public readonly MethodBase Method;
            public readonly List<ManagedDetourBoxBase> RelatedDetours = new();
            public bool IsValid = true;

            public RelatedDetourBag(MethodBase method)
                => Method = method;
        }

        private readonly ConcurrentDictionary<MethodBase, RelatedDetourBag> relatedDetours = new();
        private void AddRelatedDetour(MethodBase m, ManagedDetourBoxBase detourBox)
        {
            Retry:
            var related = relatedDetours.GetOrAdd(m, static m => new(m));
            lock (related.Lock)
            {
                if (!related.IsValid)
                    goto Retry;
                related.RelatedDetours.Add(detourBox);
                if (related.RelatedDetours.Count > 1)
                {
                    MMDbgLog.Warning($"Multiple related detours for method {m}! This means that the method has been detoured twice. Detour cleanup will fail.");
                }
            }
        }

        private void RemoveRelatedDetour(MethodBase m, ManagedDetourBoxBase cmh)
        {
            Retry:
            if (relatedDetours.TryGetValue(m, out var related))
            {
                lock (related.Lock)
                {
                    if (!related.IsValid)
                        goto Retry;
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

        private abstract class DetourBoxBase(PlatformTripleDetourFactory factory)
        {
            public readonly PlatformTripleDetourFactory Factory = factory;
            public readonly PlatformTriple Triple = factory.triple;
            public readonly List<SimpleNativeDetour> Detours = new();
            public readonly List<IDisposable> AllocHandles = new();

            public readonly Lock Sync = new();
            private bool isApplying;
            private bool isApplied;

            public bool IsApplied
            {
                get => Volatile.Read(ref isApplied);
                private set
                {
                    Volatile.Write(ref isApplied, value);
                    Thread.MemoryBarrier();
                }
            }

            public bool IsApplying
            {
                get => Volatile.Read(ref isApplying);
                protected set
                {
                    Volatile.Write(ref isApplying, value);
                    Thread.MemoryBarrier();
                }
            }

            [DoesNotReturn]
            private static void ThrowInvalidApply()
                => throw new InvalidOperationException("Cannot apply a detour which is already applied");

            public void Apply()
            {
                if (IsApplied) ThrowInvalidApply();

                lock (Sync)
                {
                    if (IsApplied)
                    {
                        ThrowInvalidApply();
                    }

                    if (IsApplying)
                    {
                        return;
                    }

                    IsApplying = true;
                    try
                    {
                        ApplyCore();
                        IsApplied = true;
                    }
                    finally
                    {
                        IsApplying = false;
                    }
                }
            }

            protected abstract void ApplyCore();

            [DoesNotReturn]
            private static void ThrowInvalidUndo()
                => throw new InvalidOperationException("Cannot undo a detour which is not applied");

            public void Undo()
            {
                if (!IsApplied) ThrowInvalidUndo();

                lock (Sync)
                {
                    if (!IsApplied)
                    {
                        ThrowInvalidUndo();
                    }

                    if (IsApplying)
                    {
                        return;
                    }

                    IsApplying = true;
                    try
                    {
                        UndoCore();
                        IsApplied = false;
                    }
                    finally
                    {
                        IsApplying = false;
                    }
                }

                OnAfterUndoOutsideLock();
            }

            protected abstract void OnBeforeUndo();
            protected abstract void OnAfterUndo();
            protected virtual void OnAfterUndoOutsideLock() { }

            // NOTE: requires lock being held
            protected void UndoCore()
            {
                OnBeforeUndo();

                IsApplied = false;

                // first clear the detours to not be in use anymore
                foreach (var detour in Detours)
                {
                    detour.Dispose();
                }
                Detours.Clear();

                // then we can clean up alloc handles, since the detours may have depended on them
                foreach (var handle in AllocHandles)
                {
                    handle.Dispose();
                }
                AllocHandles.Clear();

                OnAfterUndo();
            }
        }

        private abstract class CoreDetourBase(DetourBoxBase box) : ICoreDetourBase
        {
            private bool disposedValue;

            public DetourBoxBase DetourBox { get; } = box;

            public bool IsApplied => DetourBox.IsApplied;

            public void Apply() => DetourBox.Apply();
            public void Undo() => DetourBox.Undo();

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects)
                    }

                    if (IsApplied)
                    {
                        Undo();
                    }
                    disposedValue = true;
                }
            }

            ~CoreDetourBase()
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
        }

        private abstract class ManagedDetourBoxBase : DetourBoxBase
        {
            public readonly MethodBase SrcMethod;
            public readonly MethodBase DstMethod;

            public ManagedDetourBoxBase(
                PlatformTripleDetourFactory factory,
                MethodBase srcMethod, MethodBase dstMethod)
                : base(factory)
            {
                SrcMethod = srcMethod;
                DstMethod = dstMethod;

                if (Factory.SupportsJitHook)
                {
                    Factory.EnsureJitHook();
                    Factory.AddRelatedDetour(srcMethod, this);
                }
            }

            protected override void OnBeforeUndo()
            {
                MMDbgLog.Trace($"Undoing managed detour from {SrcMethod} to {DstMethod}");
            }
            protected override void OnAfterUndo() { }

            protected override void OnAfterUndoOutsideLock()
            {
                base.OnAfterUndoOutsideLock();

                // note: it is safe to do this outside the lock, since if we recieve a callback after we've already undone,
                // we'll bail out after acquireing that lock anyway. 

                // Further, we *need* to do this, since this avoids lock inversion in the undo path relative to
                // OnRelatedMethodCompiled:
                // - In the Undo path, we would have lock (Sync) { lock (related.Lock) { ... } }
                // - In the OnRelatedMethodCompiled path, we have lock (related.Lock) { lock (Sync) { ... } }
                // This way, we have on this path lock (Sync) { ... } lock (related.Lock) { ... } instead
                if (Factory.SupportsJitHook)
                {
                    Factory.RemoveRelatedDetour(SrcMethod, this);
                }
            }

            public abstract void OnRelatedMethodCompiled(MethodBase method, RuntimeMethodHandle handle, IntPtr codeStart, IntPtr codeRw, ulong codeSize);
        }

        private sealed class ManagedCoreDetour(ManagedDetourBoxBase box) : CoreDetourBase(box), ICoreDetour
        {
            public new ManagedDetourBoxBase DetourBox => (ManagedDetourBoxBase)base.DetourBox;

            public MethodBase Source => DetourBox.SrcMethod;

            public MethodBase Target => DetourBox.DstMethod;
        }

        public ICoreDetour CreateDetour(CreateDetourRequest request)
        {
            Helpers.ThrowIfArgumentNull(request.Source);
            Helpers.ThrowIfArgumentNull(request.Target);

            // note: we ignore request.CreateSourceCloneIfNotIlClone because we only support detouring IL methods.
            // In the future, if we decide to support non-IL normal methods, we should populate our returned objects
            // with methods that call into the original via alt entrypoints.

            var source = triple.GetIdentifiable(request.Source);
            var target = triple.GetIdentifiable(request.Target);

            ManagedCoreDetour detour;
            if (triple.SupportedFeatures.Has(RuntimeFeature.HasRuntimeAsync)
                // runtime supports runtime async, we need to check for that so we can do the right thing in that case
                && triple.Runtime.TryGetRuntimeAsyncInfo(source, out var srcAsyncInfo)
                // even though this is a method which this runtime identifies as a runtime-async method,
                // we may still be able to treat this method as a normal method. If the async variant is
                // a thin thunk to the normal variant, we can stick to only detouring the ordinary variant.
                && !srcAsyncInfo.AsyncVariantFlags.Has(RuntimeAsyncVariantFlags.IsThunk))
            {
                var ordinaryDst = target;
                var asyncDst = target;

                // this method has a real async variant that we need to detour specially.
                // In this case, the target MUST also be recognized by the runtime's async machinery,
                // though it doesn't matter whether the variants are thunks in that case.
                if (!triple.Runtime.TryGetRuntimeAsyncInfo(target, out var tgtAsyncInfo))
                {
                    // This can happen (and in fact is a fairly expected case) since when DynamicMethods return task types,
                    // CoreCLR does not generate async variants for it. 

                    MMDbgLog.Trace($"Generating async wrapper for target method {target}");
                    asyncDst = CreateAsyncMethodVariant((MethodInfo)target);
                    Helpers.Assert(triple.Runtime.TryGetRuntimeAsyncInfo(asyncDst, out tgtAsyncInfo));
                }

                detour = CreateAsyncDetour(
                    source, srcAsyncInfo,
                    target,
                    ordinaryDst,
                    asyncDst, tgtAsyncInfo);
            }
            else
            {
                detour = CreateOrdinaryDetour(source, target);
            }

            // if the creator wants it to be applied when they recieve it, apply it now
            if (request.ApplyByDefault)
            {
                detour.Apply();
            }

            return detour;
        }

        [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly",
            Justification = "the referred to argument names are for the public api surface")]
        private static AbiArgumentMap GetArgumentMap(MethodBase src, MethodBase dst)
        {

            if (!dst.IsStatic)
            {
                // dst is instance, so src must be instance
                if (src.IsStatic)
                {
                    throw new ArgumentException("Static to instance detours are not supported", "target"); 
                }

                return AbiArgumentMap.Identity;
            }
            else
            {
                // dst is static
                if (src.IsStatic)
                {
                    return AbiArgumentMap.Identity;
                }
                else
                {
                    return AbiArgumentMap.InstanceToStatic;
                }
            }

        }

        private void DisableInlining(MethodBase method, RuntimeMethodHandle handle)
        {
            if (!triple.TryDisableInlining(method, handle))
            {
                MMDbgLog.Warning($"Could not disable inlining for detoured method {method}");
            }
        }

        private ManagedCoreDetour CreateOrdinaryDetour(MethodBase src, MethodBase dst)
        {
            var srcHandle = triple.Runtime.GetMethodHandle(src);
            DisableInlining(src, srcHandle);

            var argumentMap = GetArgumentMap(src, dst);
            var realAbiTarget = triple.GetRealDetourTarget(src, dst, argumentMap);
            var targetHandle = triple.Runtime.GetMethodHandle(realAbiTarget);

            var detourBox = new OrdinaryManagedDetourBox(this,
                src, srcHandle,
                dst,
                realAbiTarget, targetHandle);

            return new ManagedCoreDetour(detourBox);
        }

        private sealed class OrdinaryManagedDetourBox : ManagedDetourBoxBase
        {
            public readonly RuntimeMethodHandle SrcMethodHandle;
            public readonly RuntimeMethodHandle RealDstMethodHandle;

            public readonly MethodBase RealDstMethod;

            private IDisposable? srcPin;
            private IDisposable? dstPin;

            public OrdinaryManagedDetourBox(PlatformTripleDetourFactory factory,
                MethodBase srcMethod, RuntimeMethodHandle srcMethodHandle,
                MethodBase origDstMethod,
                MethodBase realDstMethod, RuntimeMethodHandle realDstMethodHandle
                )
                : base(factory, srcMethod, origDstMethod)
            {
                SrcMethodHandle = srcMethodHandle;
                RealDstMethod = realDstMethod;
                RealDstMethodHandle = realDstMethodHandle;
            }

            protected override void ApplyCore()
            {
                MMDbgLog.Trace($"Applying detour from {SrcMethod} to {RealDstMethod}");

                Triple.Compile(SrcMethod, SrcMethodHandle);
                var from = Triple.GetNativeMethodBody(SrcMethod, SrcMethodHandle);
                Triple.Compile(RealDstMethod, RealDstMethodHandle);
                var to = RealDstMethodHandle.GetFunctionPointer();

                srcPin = Triple.PinMethodIfNeeded(SrcMethod);
                dstPin = Triple.PinMethodIfNeeded(RealDstMethod);

                var detour = Triple.CreateSimpleDetour(from, to);
                Detours.Add(detour);
            }

            protected override void OnAfterUndo()
            {
                base.OnAfterUndo();
                Interlocked.Exchange(ref srcPin, null)?.Dispose();
                Interlocked.Exchange(ref dstPin, null)?.Dispose();
            }

            public override void OnRelatedMethodCompiled(MethodBase method, RuntimeMethodHandle handle, nint codeStart, nint codeRw, ulong codeSize)
            {
                if (!IsApplied)
                {
                    return;
                }

                method = Triple.GetIdentifiable(method);

                if (method != SrcMethod || handle != SrcMethodHandle)
                {
                    return;
                }

                lock (Sync)
                {
                    if (!IsApplied)
                    {
                        return;
                    }
                    if (IsApplying)
                    {
                        return;
                    }

                    IsApplying = true;

                    try
                    {
                        var to = RealDstMethodHandle.GetFunctionPointer();
                        var newDetour = Triple.CreateSimpleDetour(codeStart, to, detourMaxSize: (int)codeSize, fromRw: codeRw);
                        Detours.Add(newDetour);
                    }
                    catch (Exception e)
                    {
                        MMDbgLog.Error($"An exception occurred while updating the detour from {SrcMethod} to {RealDstMethod}: {e}");
                    }
                    finally
                    {
                        IsApplying = false;
                    }
                }
            }
        }

        private static MethodInfo CreateAsyncMethodVariant(MethodInfo targetMethod)
        {
            Helpers.Assert(RuntimeAsync.IsSupportedReturnType(targetMethod.ReturnType));

            var sig = MethodSignature.ForMethod(targetMethod);
            using var dmd = sig.CreateDmd(DebugFormatter.Format($"[async]{targetMethod.Name}"));

            // we emit the equivalent of "=> await Original(...);" as an IsAsync method to ensure we get an async backend

            dmd.IsAsync = true;
            var il = dmd.GetILProcessor();
            for (var i = 0; i < sig.ParameterCount; i++)
            {
                il.Emit(OpCodes.Ldarg, i);
            }
            il.Emit(OpCodes.Call, dmd.Module.ImportReference(targetMethod));
            RuntimeAsync.EmitAwait(il, dmd.Module.ImportReference(sig.ReturnType));
            il.Emit(OpCodes.Ret);

            return dmd.Generate();
        }

        /// <param name="src"></param>
        /// <param name="srcAsyncInfo"></param>
        /// <param name="logicalDst"></param>
        /// <param name="normalDst">The <see cref="MethodBase"/> for the method used for the ordinary method of <paramref name="src"/> (when that's needed)</param>
        /// <param name="asyncDst">The <see cref="MethodBase"/> for the method used for the async method, with its RuntimeAsync info in <paramref name="dstAsyncInfo"/></param>
        /// <param name="dstAsyncInfo"></param>
        /// <remarks>
        /// If <paramref name="normalDst"/> == <paramref name="asyncDst"/>, the ordinary detour should detour to <c><paramref name="dstAsyncInfo"/>
        /// .<see cref="RuntimeAsyncMethodInfo.NormalVariant"/></c>.
        /// If they are different, then resolve <paramref name="normalDst"/>'s method handle normally and use that.
        /// The async variant of <paramref name="srcAsyncInfo"/> is always detoured to <paramref name="dstAsyncInfo"/>.<see cref="RuntimeAsyncMethodInfo.AsyncVariant"/>.
        /// </remarks>
        private ManagedCoreDetour CreateAsyncDetour(
            MethodBase src, RuntimeAsyncMethodInfo srcAsyncInfo,
            MethodBase logicalDst, MethodBase normalDst,
            MethodBase asyncDst, RuntimeAsyncMethodInfo dstAsyncInfo)
        {
            DisableInlining(src, srcAsyncInfo.NormalVariant);
            DisableInlining(src, srcAsyncInfo.AsyncVariant);

            //var normalDstHandle = normalDst == asyncDst ? dstAsyncInfo.NormalVariant : triple.Runtime.GetMethodHandle(normalDst);
            var asyncDstHandle = dstAsyncInfo.AsyncVariant;

            var normalArgMap = GetArgumentMap(src, normalDst);
            var asyncArgMap = normalDst == asyncDst ? normalArgMap : GetArgumentMap(src, asyncDst);

            // build the normal real target
            var realNormalTarget = triple.GetRealDetourTarget(src, normalDst, normalArgMap);
            var realNormalTargetHandle = triple.Runtime.GetMethodHandle(realNormalTarget);

            var detourBox = new AsyncManagedDetourBox(this,
                src, srcAsyncInfo,
                logicalDst,
                realNormalTarget, realNormalTargetHandle,
                asyncDst, asyncDstHandle,
                asyncArgMap);

            return new ManagedCoreDetour(detourBox);
        }

        private sealed class AsyncManagedDetourBox : ManagedDetourBoxBase
        {
            public readonly RuntimeAsyncMethodInfo SrcAsyncInfo;
            public readonly MethodBase NormalRealDst;
            public readonly RuntimeMethodHandle NormalRealDstHandle;
            public readonly MethodBase AsyncDst;
            public readonly RuntimeMethodHandle AsyncDstHandle;
            public readonly AbiArgumentMap AsyncArgumentMap;

            private readonly List<object> keepAlives = new();

            public AsyncManagedDetourBox(
                PlatformTripleDetourFactory factory,
                MethodBase src, RuntimeAsyncMethodInfo srcAsyncInfo,
                MethodBase logicalDst,
                MethodBase normalRealDst, RuntimeMethodHandle normalRealDstHandle,
                MethodBase asyncDst, RuntimeMethodHandle asyncDstHandle,
                AbiArgumentMap asyncArgMap)
                : base(factory, src, logicalDst)
            {
                SrcAsyncInfo = srcAsyncInfo;
                NormalRealDst = normalRealDst;
                NormalRealDstHandle = normalRealDstHandle;
                AsyncDst = asyncDst;
                AsyncDstHandle = asyncDstHandle;
                AsyncArgumentMap = asyncArgMap;
            }

            [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
                Justification = "All disposables correctly end up where they need to be, and will be cleaned up when appropriate.")]
            private unsafe (MethodInfo AsyncDst, RuntimeMethodHandle AsyncDstHandle) BuildNewAsyncVariantTarget(out IntPtr* realTargetSlot)
            {
                // we need to rebuild this each time the async variant is recompiled, since the JIT makes no attempt
                // to make new codeversions compatible with old continuation, and instead relies on OSR to make that work.
                Helpers.Assert(Triple.System.MemoryAllocator.TryAllocate(new(IntPtr.Size), out var targetSlot));
                realTargetSlot = (IntPtr*)targetSlot.BaseAddress;
                var (realAsyncDst, realAsyncDstHandle) = Triple.Runtime.GetAsyncVariantRealDetourTarget(new(
                    SrcMethod, SrcAsyncInfo.AsyncVariant,
                    AsyncDst, AsyncDstHandle,
                    targetSlot.BaseAddress),
                    AsyncArgumentMap, out var asyncTargetHandle);

                keepAlives.Add(realAsyncDst);

                AllocHandles.Add(targetSlot);
                if (asyncTargetHandle is not null)
                {
                    AllocHandles.Add(asyncTargetHandle);
                }

                var targetPin = Triple.PinMethodIfNeeded(realAsyncDst);
                if (targetPin is not null)
                {
                    AllocHandles.Add(targetPin);
                }

                return (realAsyncDst, realAsyncDstHandle);
            }

            protected override unsafe void ApplyCore()
            {
                MMDbgLog.Trace($"Applying detour from {SrcMethod} to {DstMethod}");

                // need to detour the normal variant
                Triple.Compile(SrcMethod, SrcAsyncInfo.NormalVariant);
                var normalFrom = Triple.GetNativeMethodBody(SrcMethod, SrcAsyncInfo.NormalVariant);

                Triple.Compile(NormalRealDst, NormalRealDstHandle);
                var normalTo = NormalRealDstHandle.GetFunctionPointer();

                var srcPin = Triple.PinMethodIfNeeded(SrcMethod);
                var dstPin = Triple.PinMethodIfNeeded(NormalRealDst);
                if (srcPin is not null)
                {
                    AllocHandles.Add(srcPin);
                }
                if (dstPin is not null)
                {
                    AllocHandles.Add(dstPin);
                }

                Detours.Add(Triple.CreateSimpleDetour(normalFrom, normalTo));

                // then we *also* need to generate and detour the async variant
                var (realTarget, realHandle) = BuildNewAsyncVariantTarget(out var realTargetSlot);
                Triple.Compile(SrcMethod, SrcAsyncInfo.AsyncVariant);
                var asyncFrom = Triple.GetNativeMethodBody(SrcMethod, SrcAsyncInfo.AsyncVariant);

                Triple.Compile(realTarget, realHandle);
                var asyncTo = realHandle.GetFunctionPointer();

                var newDetour = Triple.CreateNativeDetour(asyncFrom, asyncTo, ref *realTargetSlot);
                if (newDetour.AltHandle is not null)
                {
                    AllocHandles.Add(newDetour.AltHandle);
                }
                Detours.Add(newDetour.Simple);
            }

            protected override void OnAfterUndo()
            {
                base.OnAfterUndo();
                keepAlives.Clear();
            }

            public override unsafe void OnRelatedMethodCompiled(MethodBase method, RuntimeMethodHandle handle, IntPtr codeStart, IntPtr codeRw, ulong codeSize)
            {
                if (!IsApplied)
                {
                    return;
                }

                method = Triple.GetIdentifiable(method);

                if (method != SrcMethod)
                {
                    return;
                }

                lock (Sync)
                {
                    if (!IsApplied)
                    {
                        return;
                    }
                    if (IsApplying)
                    {
                        return;
                    }

                    IsApplying = true;

                    try
                    {
                        if (handle == SrcAsyncInfo.NormalVariant)
                        {
                            // recompiling the normal variant
                            var to = NormalRealDstHandle.GetFunctionPointer();
                            var newDetour = Triple.CreateSimpleDetour(codeStart, to, detourMaxSize: (int)codeSize, fromRw: codeRw);
                            Detours.Add(newDetour);
                        }
                        else if (handle == SrcAsyncInfo.AsyncVariant)
                        {
                            // recompiling the async variant
                            // we need to generate a NEW real target to detour to, since runtime async continuations are not code-version compatible by default
                            var (realTarget, realHandle) = BuildNewAsyncVariantTarget(out var realTargetSlot);
                            MMDbgLog.Trace($"Updating async variant of {SrcMethod} detour to {realTarget}:{realHandle.Value:x}");
                            var to = realHandle.GetFunctionPointer();
                            var newDetour = Triple.CreateNativeDetour(codeStart, to, ref *realTargetSlot, detourMaxSize: (int)codeSize, fromRw: codeRw);
                            if (newDetour.AltHandle is not null)
                            {
                                AllocHandles.Add(newDetour.AltHandle);
                            }
                            Detours.Add(newDetour.Simple);
                        }
                    }
                    catch (Exception e)
                    {
                        MMDbgLog.Error($"An exception occurred while updating the detour from {SrcMethod} to {DstMethod}: {e}");
                    }
                    finally
                    {
                        IsApplying = false;
                    }
                }
            }
        }

        private sealed class NativeDetourBox : DetourBoxBase
        {
            public readonly IntPtr SrcAddress;
            public readonly IntPtr DstAddress;
            public IntPtr OrigEntrypoint;

            public NativeDetourBox(PlatformTripleDetourFactory factory, IntPtr srcAddress, IntPtr dstAddress)
                : base(factory)
            {
                SrcAddress = srcAddress;
                DstAddress = dstAddress;
            }

            protected override void ApplyCore()
            {
                MMDbgLog.Trace($"Applying native detour from {SrcAddress:x16} to {DstAddress:x16}");

                var (simple, altEntry, altHandle) = Triple.CreateNativeDetour(SrcAddress, DstAddress);
                Detours.Add(simple);
                OrigEntrypoint = altEntry;
                if (altHandle is not null)
                {
                    AllocHandles.Add(altHandle);
                }
            }

            protected override void OnBeforeUndo()
            {
                MMDbgLog.Trace($"Undoing native detour from {SrcAddress} to {DstAddress}");
                OrigEntrypoint = IntPtr.Zero;
            }

            protected override void OnAfterUndo()
            {
            }
        }

        private sealed class CoreNativeDetour(NativeDetourBox box) : CoreDetourBase(box), ICoreNativeDetour
        {
            public new NativeDetourBox DetourBox => (NativeDetourBox)base.DetourBox;

            public IntPtr Source => DetourBox.SrcAddress;
            public IntPtr Target => DetourBox.DstAddress;

            public bool HasOrigEntrypoint => DetourBox.OrigEntrypoint != IntPtr.Zero;
            public IntPtr OrigEntrypoint => DetourBox.OrigEntrypoint;
        }


        public ICoreNativeDetour CreateNativeDetour(CreateNativeDetourRequest request)
        {
            if (request.Source == request.Target)
            {
                throw new ArgumentException("Cannot detour a function to itself!");
            }

            var box = new NativeDetourBox(this, request.Source, request.Target);
            var detour = new CoreNativeDetour(box);

            if (request.ApplyByDefault)
            {
                detour.Apply();
            }

            return detour;
        }
    }
}
