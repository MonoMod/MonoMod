using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.RuntimeDetour.Generics
{
    internal sealed partial class SharedBodyDispatcher
    {
        // One per distinct shared native body pointer. Process-wide, refcounted by layers.
        private sealed class SharedBody
        {
            public int BodyId { get; }
            internal readonly IntPtr bodyPtr;
            private readonly MethodBase openSource;
            private readonly GenericContextReader.GenericContextKind kind;
            private readonly ICoreNativeDetour detour;
            private readonly object dispatchKeepAlive;
            private readonly GenericContextCaptureStub? entryStub;
            // MISS forward target, re-pointed to the divergent body's orig once detoured.
            private IntPtr missEntry;
            private readonly IntPtr detourTarget;
            private readonly object divergentLock = new();
            // Divergent concrete bodies detoured, mapped to their detour; reference instantiations share one.
            private readonly Dictionary<IntPtr, ICoreNativeDetour> divergentBodyDetours = [];
            private readonly HashSet<IntPtr> detouredAddrs = [];
            // Tier-up re-detours, additive over _detour.
            private readonly List<PlatformTriple.NativeDetour> retierDetours = [];
            private readonly ConcurrentDictionary<InstantiationKey, ICoreNativeDetour> divergentDetours = [];
            private readonly bool dropsCtx;
            // Mono gsharedvt: the context is unrecoverable and reading it can abort the runtime,
            // so dispatch by the single registered instantiation (each value instantiation has its own detoured body).
            private readonly bool isValueTypeShared;
            private readonly ConcurrentDictionary<InstantiationKey, (IntPtr Entry, object KeepAlive)> origClones = [];

            private readonly ConcurrentDictionary<InstantiationKey, KeyChain> entries = [];
            // Registered instantiation's runtime pointer (MethodTable*/MethodDesc*) -> key; the generic context is that pointer,
            // so resolution is a pointer comparison, avoiding handle->Type conversions that fault on .NET 5/6.
            private readonly ConcurrentDictionary<IntPtr, InstantiationKey> contextPtrToKey = [];
            private readonly ConcurrentDictionary<SharedBodyDispatcher, byte> owners = [];
            private readonly ConcurrentBag<(GCHandle Handle, object? KeepAlive)> retired = [];

            private readonly object _lock = new();
            private int layerCount;
            private bool disposed;

            private bool UseContextPointerMatch
                => PlatformDetection.Runtime != RuntimeKind.Mono
                   && kind is GenericContextReader.GenericContextKind.MethodTable or GenericContextReader.GenericContextKind.MethodDesc;

            private bool TryGetIdentityPointer(MethodBase closedSource, out IntPtr ptr)
            {
                ptr = IntPtr.Zero;
                try
                {
                    switch (kind)
                    {
                        case GenericContextReader.GenericContextKind.MethodTable:
                            if (closedSource.DeclaringType is { } dt)
                            {
                                ptr = dt.TypeHandle.Value;
                                return true;
                            }
                            return false;
                        case GenericContextReader.GenericContextKind.MethodDesc:
                            ptr = closedSource.MethodHandle.Value;
                            return true;
                        default:
                            return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            // Real JITed code address when the detoured body is a stub/precode in front of it;
            // with-orig calls it directly to avoid the stub's looping alt-entry.
            private readonly IntPtr realBody;
            private readonly string? canonKey;
            private readonly IntPtr registrationKey;

            private SharedBody(int bodyId, IntPtr bodyPtr, IntPtr registrationKey, MethodBase openSource, GenericContextReader.GenericContextKind kind,
                ICoreNativeDetour detour, object dispatchKeepAlive, GenericContextCaptureStub? entryStub, IntPtr missEntry, bool dropsCtx,
                IntPtr detourTarget, bool isValueTypeShared, IntPtr realBody, string? canonKey)
            {
                BodyId = bodyId;
                this.bodyPtr = bodyPtr;
                this.registrationKey = registrationKey;
                this.openSource = openSource;
                this.kind = kind;
                this.detour = detour;
                this.dispatchKeepAlive = dispatchKeepAlive;
                this.entryStub = entryStub;
                this.missEntry = missEntry;
                this.dropsCtx = dropsCtx;
                this.detourTarget = detourTarget;
                this.isValueTypeShared = isValueTypeShared;
                this.realBody = realBody;
                this.canonKey = canonKey;
                detouredAddrs.Add(bodyPtr);
                if (registrationKey != IntPtr.Zero)
                {
                    detouredAddrs.Add(registrationKey);
                }
                if (realBody != IntPtr.Zero)
                {
                    detouredAddrs.Add(realBody);
                }
            }

            private static bool ContainsValueTypeArg(MethodBase closedSource)
            {
                if (closedSource.DeclaringType is { IsGenericType: true } dt)
                {
                    foreach (var a in dt.GetGenericArguments())
                    {
                        if (a.IsValueType)
                        {
                            return true;
                        }
                    }
                }
                if (closedSource.IsGenericMethod)
                {
                    foreach (var a in closedSource.GetGenericArguments())
                    {
                        if (a.IsValueType)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            private static bool KeyHasValueTypeArg(InstantiationKey key)
            {
                foreach (var t in key.TypeArguments)
                {
                    if (t.IsValueType)
                    {
                        return true;
                    }
                }
                return false;
            }

            public static SharedBody Create(IntPtr bodyPtr, MethodBase closedSource, MethodBase openSource, GenericContextReader.GenericContextKind kind, IntPtr realBody, string? canonKey)
            {
                var bodyId = Interlocked.Increment(ref nextBodyId);
                var dispatcher = UniversalDispatcher.Build(closedSource, kind, bodyId);

                // Mono passes the context in a dedicated register for everything but ThisObject;
                // bridge it with a native capture stub. Elsewhere the body is detoured straight to the managed dispatcher.
                var dropsCtx = PlatformDetection.Runtime == RuntimeKind.Mono && kind != GenericContextReader.GenericContextKind.ThisObject;
                GenericContextCaptureStub? entryStub = null;
                IntPtr detourTarget;
                if (dropsCtx)
                {
                    entryStub = new GenericContextCaptureStub(dispatcher.Entry, dispatcher.ContextArgRegisterIndex);
                    detourTarget = entryStub.Address;
                }
                else
                {
                    detourTarget = dispatcher.Entry;
                }

                var isValueTypeShared = dropsCtx && ContainsValueTypeArg(closedSource);

                // Detour the real JITed shared code (compile-hook codeStart) when the walked body is a stub/precode in front of it. 
                var haveReal = realBody != IntPtr.Zero && realBody != bodyPtr;
                var detourFrom = haveReal ? realBody : bodyPtr;
                ICoreNativeDetour detour;
                var detouredBody = detourFrom;
                // orig calls the real code directly only when a (non-precode) stub was detoured, whose alt-entry would loop;
                // when the real code itself was detoured, orig uses the detour's own alt-entry.
                var origRealBody = detourFrom == realBody ? IntPtr.Zero : realBody;
                try
                {
                    detour = DetourFactory.Current.CreateNativeDetour(detourFrom, detourTarget);
                }
                catch (InvalidOperationException) when (realBody != IntPtr.Zero && realBody != detourFrom)
                {
                    detour = DetourFactory.Current.CreateNativeDetour(realBody, detourTarget);
                    detouredBody = realBody;
                    origRealBody = IntPtr.Zero;
                }
                // Mono uses a per-instantiation clone instead (the original body needs the context register),
                // so _missEntry is unused there. When a stub distinct from the real code was detoured,
                // forward to the real code directly, else the detour's own alt-entry.
                var missEntry = dropsCtx ? IntPtr.Zero : (origRealBody != IntPtr.Zero ? origRealBody : detour.OrigEntrypoint);
                return new SharedBody(bodyId, detouredBody, bodyPtr, openSource, kind, detour, dispatcher.KeepAlive, entryStub, missEntry, dropsCtx, detourTarget, isValueTypeShared, origRealBody, canonKey);
            }

            public void AddOwner(SharedBodyDispatcher d) => owners.TryAdd(d, 0);
            public void RemoveOwner(SharedBodyDispatcher d) => owners.TryRemove(d, out _);

            public IDisposable AddLayer(InstantiationKey key, MethodBase closedSource, MethodInfo closedTarget, bool wantsOrig)
            {
                lock (_lock)
                {
                    var chain = entries.GetOrAdd(key, k => new KeyChain(this, k, closedSource));

                    if (UseContextPointerMatch && TryGetIdentityPointer(closedSource, out var idPtr) && idPtr != IntPtr.Zero)
                    {
                        contextPtrToKey[idPtr] = key;
                    }

                    EnsureDivergentBodyDetoured(key, closedSource);

                    var layer = chain.AddLayer(closedTarget, wantsOrig);
                    layerCount += 1;
                    WatchInstantiation(closedSource, this);
                    return new LayerHandle(this, chain, layer);
                }
            }

            // Fires when a body for a watched open generic method is JIT-compiled, before it runs.
            internal void TryDetourCompiledInstantiation(MethodBase compiled, Type[] methodArgs, IntPtr codeStart, IntPtr codeStartRw, ulong codeSize)
            {
                if (disposed || codeStart == IntPtr.Zero || dropsCtx)
                {
                    return;
                }

                // Tiered compilation recompiled the shared __Canon body at a new address; re-detour it.
                if (PlatformDetection.Runtime == RuntimeKind.CoreCLR && canonKey is not null
                    && IsCanonicalInstantiation(compiled) && CanonKey(compiled) == canonKey)
                {
                    OnBodyRecompiled(codeStart, codeStartRw, codeSize);
                    return;
                }

                if (codeStart == bodyPtr)
                {
                    return;
                }

                var declArgs = compiled.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : Type.EmptyTypes;
                Type[] full = declArgs.Length == 0 ? methodArgs : [.. declArgs, .. methodArgs];
                var key = new InstantiationKey(full);

                if (!entries.ContainsKey(key))
                {
                    return;
                }

                // Only detour bodies that carry a generic context (same ABI as __Canon);
                // an unshared value-type body has no context arg and routing it would misalign the ABI.
                bool hasCtx;
                try
                {
                    hasCtx = PlatformTriple.Current.Runtime.RequiresGenericContext(compiled);
                }
                catch
                {
                    hasCtx = false;
                }
                if (!hasCtx)
                {
                    return;
                }

                lock (divergentLock)
                {
                    MapDivergentDetour(key, codeStart);
                }
            }

            // Build orig as a direct call into the original shared body with this instantiation's fixed generic
            // context, instead of cloning the concrete method's IL. Only needed on .NET Core (RequiresBodyThunkWalking),
            // where cloning the concrete instantiation makes the runtime route the call around the canonical detour
            // (and crashes on 3.x). Elsewhere the clone (EnsureBaseClone) is used.
            internal bool TryBuildOrigViaBody(MethodBase closedSource, InstantiationKey key, Type origDelType, out Delegate orig, out object? keepAlive)
            {
                orig = null!;
                keepAlive = null;
                if (PlatformDetection.Runtime != RuntimeKind.CoreCLR
                    || dropsCtx
                    || kind == GenericContextReader.GenericContextKind.ThisObject
                    || !PlatformTriple.Current.SupportedFeatures.Has(Core.Platforms.RuntimeFeature.RequiresBodyThunkWalking))
                {
                    return false;
                }
                if (!TryGetIdentityPointer(closedSource, out var ctx) || ctx == IntPtr.Zero)
                {
                    return false;
                }
                // If this instantiation runs on its own detoured divergent body, orig must skip that detour and call
                // the concrete body's OrigEntrypoint, or it recurses.
                var origEntry = detour.OrigEntrypoint;
                if (divergentDetours.TryGetValue(key, out var divergent) && divergent.OrigEntrypoint != IntPtr.Zero)
                {
                    origEntry = divergent.OrigEntrypoint;
                }
                else
                {
                    // The detoured body may be a stub/precode whose alt-entry loops; call the real JITed code
                    // directly, falling back to a fresh lookup for a body created by an earlier hook.

                    var rBody = realBody != IntPtr.Zero ? realBody : GetRecordedCanonRealBody(openSource, closedSource);
                    if (rBody != IntPtr.Zero && rBody != bodyPtr)
                    {
                        origEntry = rBody;
                    }
                }
                if (origEntry == IntPtr.Zero)
                {
                    return false;
                }
                try
                {
                    (orig, keepAlive) = UniversalDispatcher.BuildOrigViaBody(closedSource, origEntry, ctx, origDelType);
                    return true;
                }
                catch (Exception ex)
                {
                    MMDbgLog.Warning($"Orig-via-body failed for {closedSource}, falling back to clone: {ex}");
                    orig = null!;
                    keepAlive = null;
                    return false;
                }
            }

            // .NET Core instance/virtual generic methods (and static mixed <ref,value> keys) run on their own
            // divergent body instead of the detoured __Canon body, bypassing the canon detour. Follow the concrete
            // instantiation's instantiating stub to that body and detour it to the same dispatcher.
            private void EnsureDivergentBodyDetoured(InstantiationKey key, MethodBase closedSource)
            {
                if (PlatformDetection.Runtime != RuntimeKind.CoreCLR || dropsCtx)
                {
                    return;
                }

                // Force-materialise so the no-reload walk can follow the instantiating stub.
                // On .NET Core 3.x, compiling a shared generic instantiation access-violates once its body is
                // detoured, so skip there and let the JIT compile hook catch the divergent body instead.
                if (PlatformDetection.RuntimeVersion.Major != 3)
                {
                    try
                    {
                        PlatformTriple.Current.Compile(closedSource);
                    }
                    catch (Exception ex)
                    {
                        MMDbgLog.Trace($"Divergent-body force-compile failed for {closedSource}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                IntPtr concrete;
                bool followed;
                try
                {
                    concrete = PlatformTriple.Current.GetSharedGenericBodyNoReload(closedSource, out followed);
                }
                catch (Exception ex)
                {
                    MMDbgLog.Trace($"Divergent-body resolve failed for {closedSource}: {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (!followed || concrete == IntPtr.Zero || concrete == bodyPtr)
                {
                    return;
                }

                lock (divergentLock)
                {
                    MapDivergentDetour(key, concrete);
                }
            }

            // The shared __Canon body was re-JITed at `newCodeStart`;
            // reference instantiations now route there, bypassing the original detour.
            // Add a detour on the new code to the same dispatcher;
            // the original _detour stays applied for stragglers on the old code.
            // Writes through the writable alias `newCodeStartRw` since W^X JIT code (e.g. Apple-Silicon arm64) is execute-only
            // and patching the exec address corrupts it.
            private void OnBodyRecompiled(IntPtr newCodeStart, IntPtr newCodeStartRw, ulong codeSize)
            {
                lock (divergentLock)
                {
                    if (disposed || newCodeStart == IntPtr.Zero)
                    {
                        return;
                    }
                    if (!detouredAddrs.Add(newCodeStart))
                    {
                        return;
                    }
                    PlatformTriple.NativeDetour nd;
                    try
                    {
                        nd = PlatformTriple.Current.CreateNativeDetour(newCodeStart, detourTarget, (int)codeSize, newCodeStartRw);
                    }
                    catch (Exception ex)
                    {
                        detouredAddrs.Remove(newCodeStart);
                        MMDbgLog.Warning($"Failed to re-detour recompiled body 0x{newCodeStart:x16}: {ex.GetType().Name}");
                        return;
                    }
                    retierDetours.Add(nd);
                    if (nd.HasAltEntry && divergentBodyDetours.Count == 0)
                    {
                        missEntry = nd.AltEntry;
                    }
                    MMDbgLog.Trace($"{openSource} __Canon body recompiled => re-detour 0x{newCodeStart:x16} => 0x{detourTarget:x16}");
                }
            }

            private void MapDivergentDetour(InstantiationKey key, IntPtr concrete)
            {
                if (disposed)
                {
                    return;
                }
                if (divergentBodyDetours.TryGetValue(concrete, out var existing))
                {
                    divergentDetours[key] = existing;
                    return;
                }
                try
                {
                    var d = DetourFactory.Current.CreateNativeDetour(concrete, detourTarget);
                    divergentBodyDetours[concrete] = d;
                    divergentDetours[key] = d;
                    if (d.OrigEntrypoint != IntPtr.Zero)
                    {
                        missEntry = d.OrigEntrypoint;
                    }
                    MMDbgLog.Trace($"{openSource} {key} divergent body 0x{concrete:x16} => 0x{detourTarget:x16}");
                }
                catch (Exception ex)
                {
                    MMDbgLog.Warning($"Failed to detour divergent body 0x{concrete:x16}: {ex}");
                }
            }

            private void RemoveLayer(KeyChain chain, KeyChain.Layer layer)
            {
                lock (_lock)
                {
                    if (disposed)
                    {
                        return;
                    }

                    var emptied = chain.RemoveLayer(layer);
                    if (emptied)
                    {
                        UnwatchInstantiation(chain.ClosedSource, this);
                        entries.TryRemove(chain.Key, out _);
                        chain.Dispose();
                    }

                    layerCount -= 1;
                    if (layerCount == 0)
                    {
                        Teardown();
                    }
                }
            }

            // Non-ThisObject: context is a raw MethodTable*/MethodDesc* (CoreCLR/Framework arg, or Mono RGCTX reg).
            public IntPtr Resolve(IntPtr contextPtr)
            {
                // Mono gsharedvt: Reading the context faults.
                if (isValueTypeShared)
                {
                    return TryGetSingleActiveEntry(out var entry) ? entry : IntPtr.Zero;
                }

                if (inResolve)
                {
                    return MissEntry(contextPtr);
                }

                inResolve = true;
                try
                {
                    // Safe fast path: match the context pointer against the registered instantiations by pointer.
                    // sidestepping problematic handle->Type/Method conversions on .NET 5/6.
                    if (UseContextPointerMatch && contextPtrToKey.TryGetValue(contextPtr, out var matchedKey))
                    {
                        return ResolveCore(matchedKey.TypeArguments);
                    }

                    return ResolveCore(GenericContextReader.Read(contextPtr, kind, openSource));
                }
                catch (Exception ex)
                {
                    // Mono gsharedvt whose read threw: each value-type instantiation has its own body,
                    // so with exactly one active instantiation the dispatch is unambiguous.
                    if (dropsCtx && TryGetSingleActiveEntry(out var soleEntry))
                    {
                        return soleEntry;
                    }
                    MMDbgLog.Warning($"Exception in generic dispatch resolve:\n{ex}");
                }
                finally
                {
                    inResolve = false;
                }

                return MissEntry(contextPtr);
            }

            // Returns the single active (non-zero) per-key entry if this body has exactly one, else false.
            private bool TryGetSingleActiveEntry(out IntPtr entry)
            {
                entry = IntPtr.Zero;
                var count = 0;
                foreach (var chain in entries.Values)
                {
                    if (chain.Entry == IntPtr.Zero)
                    {
                        continue;
                    }
                    if (++count > 1)
                    {
                        return false;
                    }
                    entry = chain.Entry;
                }
                return count == 1;
            }

            // Where to forward when no hook matches. Prefer the original body via its native OrigEntrypoint;
            // fall back to a concrete per-instantiation clone when there is no native entry to forward to
            // (Mono non-ThisObject, or no alt-entry factory)
            private IntPtr MissEntry(IntPtr contextPtr)
            {
                if (!dropsCtx && missEntry != IntPtr.Zero)
                {
                    return missEntry;
                }
                try
                {
                    var typeArgs = GenericContextReader.Read(contextPtr, kind, openSource);
                    return GetOrBuildOrigClone(new InstantiationKey(typeArgs), typeArgs);
                }
                catch (Exception ex)
                {
                    MMDbgLog.Warning($"Failed to build orig clone:\n{ex}");
                    return IntPtr.Zero;
                }
            }

            private IntPtr MissEntry(InstantiationKey key, Type[] typeArgs) => (dropsCtx || missEntry == IntPtr.Zero) ? GetOrBuildOrigClone(key, typeArgs) : missEntry;

            private IntPtr GetOrBuildOrigClone(InstantiationKey key, Type[] typeArgs)
            {
                if (origClones.TryGetValue(key, out var existing))
                {
                    return existing.Entry;
                }

                var closedSource = GenericInstantiator.CloseSource(openSource, typeArgs);
                using var dmd = new DynamicMethodDefinition(closedSource);
                var clone = DMDCecilGenerator.Generate(dmd);
                RuntimeHelpers.PrepareMethod(clone.MethodHandle);

                var ctxIsForwarded = GenericHelper.IsContextForwarded(kind);

                IntPtr entry;
                object keepAlive;
                if (ctxIsForwarded)
                {
                    var tramp = SharedDispatchTrampoline.Build(closedSource, clone, null, kind);
                    entry = tramp.Entry;
                    object[] keep = [tramp.KeepAlive, clone];
                    keepAlive = keep;
                }
                else
                {
                    entry = clone.MethodHandle.GetFunctionPointer();
                    keepAlive = clone;
                }

                return origClones.GetOrAdd(key, (entry, keepAlive)).Entry;
            }

            public IntPtr ResolveFromThisObject(object self)
            {
                if (inResolve)
                {
                    return missEntry;
                }

                inResolve = true;
                try
                {
                    return ResolveCore(GenericContextReader.FromThisObject(self, openSource));
                }
                catch (Exception ex)
                {
                    MMDbgLog.Warning($"Exception in generic dispatch resolve:\n{ex}");
                }
                finally
                {
                    inResolve = false;
                }

                return missEntry;
            }

            private IntPtr ResolveCore(Type[] typeArgs)
            {
                var key = new InstantiationKey(typeArgs);

                if (entries.TryGetValue(key, out var chain) && chain.Entry != IntPtr.Zero)
                {
                    return chain.Entry;
                }

                var primed = false;
                // Auto-discovery varies reference positions and holds value positions fixed
                // every key reaching one shared body shares its value signature by construction.
                // Value-containing keys are gated off only on Mono (unreadable gsharedvt context).
                var valueKeyGatedOff = KeyHasValueTypeArg(key) && !GenericHook.SupportsValueTypeAutoPrime;
                if (!valueKeyGatedOff)
                {
                    foreach (var kv in owners)
                    {
                        var owner = kv.Key;
                        if (owner.AutoPrime == AutoPrimeMode.All)
                        {
                            owner.OwnerHook.OnDiscovered(key);
                            primed = true;
                        }
                        else if (owner.AutoPrime == AutoPrimeMode.Compatible)
                        {
                            foreach (var p in owner.OwnerHook.Primed)
                            {
                                // Value arg matches exactly; reference arg IsAssignableFrom
                                if (InstantiationKey.Matches(p.Key, key))
                                {
                                    owner.OwnerHook.OnDiscovered(key);
                                    primed = true;
                                }
                            }
                        }
                    }
                }

                if (primed && entries.TryGetValue(key, out chain) && chain.Entry != IntPtr.Zero)
                {
                    return chain.Entry;
                }

                return MissEntry(key, typeArgs);
            }

            internal void Retire(GCHandle handle, object? keepAlive) => retired.Add((handle, keepAlive));

            internal SharedDispatchTrampoline.Result BuildTop(MethodBase closedSource, MethodInfo closedTarget, Delegate? orig) => BuildTopEntry(closedSource, closedTarget, orig, kind);

            private void Teardown()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;

                lock (bodyLock)
                {
                    bodiesByPtr.TryRemove(registrationKey, out _);
                    bodiesById.TryRemove(BodyId, out _);
                }
                if (canonKey is not null)
                {
                    canonResolution.TryRemove(canonKey, out _);
                }

                detour.Dispose();
                lock (divergentLock)
                {
                    foreach (var d in divergentBodyDetours.Values)
                    {
                        d.Dispose();
                    }
                    divergentBodyDetours.Clear();
                    divergentDetours.Clear();
                    foreach (var nd in retierDetours)
                    {
                        nd.Simple.Dispose();
                        nd.AltHandle?.Dispose();
                    }
                    retierDetours.Clear();
                }
                entryStub?.Dispose();
                GC.KeepAlive(dispatchKeepAlive);
                origClones.Clear();

                foreach (var chain in entries.Values)
                {
                    UnwatchInstantiation(chain.ClosedSource, this);
                    chain.Dispose();
                }
                entries.Clear();

                foreach (var (handle, _) in retired)
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                }
            }

            private sealed class LayerHandle : IDisposable
            {
                private readonly SharedBody body;
                private readonly KeyChain chain;
                private readonly KeyChain.Layer layer;
                private bool done;

                public LayerHandle(SharedBody body, KeyChain chain, KeyChain.Layer layer)
                {
                    this.body = body;
                    this.chain = chain;
                    this.layer = layer;
                }

                public void Dispose()
                {
                    if (done)
                    {
                        return;
                    }
                    done = true;
                    body.RemoveLayer(chain, layer);
                }
            }
        }
    }
}
