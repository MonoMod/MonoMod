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
    /// <summary>
    /// Owns the shared native dispatch for one open generic source. The native shared body is process-global (one
    /// native detour per distinct body pointer, refcounted by live layers), so multiple <see cref="GenericHook"/>s
    /// over the same closed instantiation stack into a single per-key chain.
    /// Each instance (one per <see cref="GenericHook"/>) owns a set of layers and removes exactly those on dispose.
    /// </summary>
    internal sealed partial class SharedBodyDispatcher : IDisposable
    {
        private static readonly ConcurrentDictionary<IntPtr, SharedBody> bodiesByPtr = [];
        private static readonly ConcurrentDictionary<int, SharedBody> bodiesById = [];
        private static int nextBodyId;
        private static readonly object bodyLock = new();

        // Watched open generic-method definitions -> the SharedBodies handling their instantiations. On .NET Core an
        // instance/virtual instantiation runs on its own divergent body, compiled lazily on the first (bypassing)
        // call; the compile hook matches it by type arguments and detours it.
        private static readonly ConcurrentDictionary<MethodBase, ConcurrentDictionary<SharedBody, byte>> watchByOpenDef = [];
        private static int compileHookSubscribed;

        private static MethodBase? OpenDefOf(MethodBase m)
        {
            try
            {
                return m is MethodInfo mi && m.IsGenericMethod && !m.IsGenericMethodDefinition ? mi.GetGenericMethodDefinition() : null;
            }
            catch
            {
                return null;
            }
        }

        // Null on runtimes without __Canon (Mono).
        private static readonly Type? canonType = typeof(object).Assembly.GetType("System.__Canon");

        private static bool IsCanonicalInstantiation(MethodBase m)
        {
            if (canonType is null)
            {
                return false;
            }
            try
            {
                if (m.IsGenericMethod && !m.IsGenericMethodDefinition && Array.IndexOf(m.GetGenericArguments(), canonType) >= 0)
                {
                    return true;
                }
                return m.DeclaringType is { IsGenericType: true, ContainsGenericParameters: false } dt
                    && Array.IndexOf(dt.GetGenericArguments(), canonType) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureCompileHookSubscribed()
        {
            if (Interlocked.CompareExchange(ref compileHookSubscribed, 1, 0) != 0)
            {
                return;
            }
            var triple = PlatformTriple.Current;
            if (triple.SupportedFeatures.Has(Core.Platforms.RuntimeFeature.CompileMethodHook))
            {
                triple.Runtime.OnMethodCompiled += OnAnyMethodCompiled;
            }
        }

        private static void OnAnyMethodCompiled(RuntimeMethodHandle methodHandle, MethodBase? method, IntPtr codeStart, IntPtr codeStartRw, ulong codeSize)
        {
            if (method is null)
            {
                return;
            }
            if (codeStart != IntPtr.Zero && IsCanonicalInstantiation(method))
            {
                try
                {
                    var ck = CanonKey(method);
                    if (ck is not null)
                    {
                        canonRealBody[ck] = codeStart;
                    }
                }
                catch { }
            }
            if (watchByOpenDef.IsEmpty)
            {
                return;
            }
            try
            {
                var openDef = OpenDefOf(method);
                if (openDef is null || !watchByOpenDef.TryGetValue(openDef, out var bodies))
                {
                    return;
                }
                var methodArgs = method.GetGenericArguments();
                foreach (var kv in bodies)
                {
                    kv.Key.TryDetourCompiledInstantiation(method, methodArgs, codeStart, codeStartRw, codeSize);
                }
            }
            catch { }
        }

        private static void WatchInstantiation(MethodBase closedSource, SharedBody body)
        {
            var openDef = OpenDefOf(closedSource);
            if (openDef is null)
            {
                return;
            }

            // Only available where the runtime exposes CompileMethodHook;
            // Mono has none, so an instantiation there is reached only via its canonical detour or explicit priming.
            if (!PlatformTriple.Current.SupportedFeatures.Has(Core.Platforms.RuntimeFeature.CompileMethodHook))
            {
                return;
            }
            EnsureCompileHookSubscribed();

            var set = watchByOpenDef.GetOrAdd(openDef, static _ => []);
            set[body] = 0;
        }

        private static void UnwatchInstantiation(MethodBase closedSource, SharedBody body)
        {
            var openDef = OpenDefOf(closedSource);
            if (openDef is null)
            {
                return;
            }
            if (watchByOpenDef.TryGetValue(openDef, out var set))
            {
                set.TryRemove(body, out _);
                if (set.IsEmpty)
                {
                    watchByOpenDef.TryRemove(openDef, out _);
                }
            }
        }

        private readonly MethodBase openSource;
        private readonly GenericContextReader.GenericContextKind _kind;

        internal AutoPrimeMode AutoPrime { get; }
        internal GenericHook OwnerHook { get; }

        [ThreadStatic]
        private static bool inResolve;

        private readonly Dictionary<InstantiationKey, (SharedBody Body, IDisposable Layer)> owned = [];
        private readonly HashSet<SharedBody> touchedBodies = [];
        private readonly object _lock = new();
        private bool disposed;

        public SharedBodyDispatcher(MethodBase openSource, AutoPrimeMode autoPrime, GenericHook owner)
        {
            this.openSource = openSource;
            _kind = GenericContextReader.ClassifyContext(openSource);
            AutoPrime = autoPrime;
            OwnerHook = owner;
            EnsureCompileHookSubscribed();
        }

        public bool Contains(InstantiationKey key)
        {
            lock (_lock)
            {
                return owned.ContainsKey(key);
            }
        }

        public void Register(InstantiationKey key, MethodBase closedSource, MethodInfo closedTarget, bool wantsOrig)
        {
            Helpers.ThrowIfArgumentNull(closedSource);
            Helpers.ThrowIfArgumentNull(closedTarget);

            if (RequiresUnportedMonoCaptureStub)
            {
                throw new PlatformNotSupportedException($"Generic hooking is not supported here: {PlatformDetection.OS}, {PlatformDetection.Runtime}, {PlatformDetection.Architecture}");
            }

            Compile(closedTarget);

            lock (_lock)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(SharedBodyDispatcher));
                }

                if (owned.ContainsKey(key))
                {
                    return;
                }

                var body = AcquireBody(closedSource);
                body.AddOwner(this);
                touchedBodies.Add(body);

                var layer = body.AddLayer(key, closedSource, closedTarget, wantsOrig);
                owned[key] = (body, layer);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;

                foreach (var t in owned.Values)
                {
                    t.Layer.Dispose();
                }
                owned.Clear();

                foreach (var body in touchedBodies)
                {
                    body.RemoveOwner(this);
                }
                touchedBodies.Clear();
            }
        }

        private SharedBody AcquireBody(MethodBase closedSource)
        {
            var (bodyPtr, realBody, canonKey) = ResolveBodyPtr(closedSource);

            if (bodiesByPtr.TryGetValue(bodyPtr, out var existing))
            {
                return existing;
            }

            lock (bodyLock)
            {
                if (bodiesByPtr.TryGetValue(bodyPtr, out existing))
                {
                    return existing;
                }

                var body = SharedBody.Create(bodyPtr, closedSource, openSource, _kind, realBody, canonKey);
                bodiesByPtr[bodyPtr] = body;
                bodiesById[body.BodyId] = body;
                return body;
            }
        }

        // Resolves the shared native body for a closed instantiation, preferring the canonical (__Canon-filled) instantiation
        private (IntPtr Body, IntPtr RealBody, string? CanonKey) ResolveBodyPtr(MethodBase closedSource)
        {
            var canonical = TryGetCanonicalSource(openSource, closedSource);
            string? canonKey = null;
            if (canonical is not null)
            {
                // Resolve each canon once and reuse for siblings: re-compiling an already-JITed shared instantiation
                // crashes natively on .NET Core 3.x once its body is detoured.
                canonKey = CanonKey(canonical);
                if (canonKey is not null && canonResolution.TryGetValue(canonKey, out var cachedBody))
                {
                    var cachedReal = canonRealBody.TryGetValue(canonKey, out var cr) && cr != cachedBody ? cr : IntPtr.Zero;
                    return (cachedBody, cachedReal, canonKey);
                }

                try
                {
                    Compile(canonical);
                    var body = PlatformTriple.Current.GetNativeMethodBody(canonical);
                    var realBody = canonKey is not null && canonRealBody.TryGetValue(canonKey, out var rb) && rb != body ? rb : IntPtr.Zero;
                    if (canonKey is not null)
                    {
                        canonResolution[canonKey] = body;
                    }
                    return (body, realBody, canonKey);
                }
                catch (Exception ex)
                {
                    MMDbgLog.Trace($"Canonical shared-body resolution failed for {closedSource}; using concrete instantiation. {ex.GetType().Name}: {ex.Message}");
                }
            }

            Compile(closedSource);
            return (PlatformTriple.Current.GetNativeMethodBody(closedSource), IntPtr.Zero, canonKey);
        }

        // Canon identity -> resolved shared body ptr.
        private static readonly ConcurrentDictionary<string, IntPtr> canonResolution = [];

        // Canon identity -> real JITed code address, recorded by OnAnyMethodCompiled.
        private static readonly ConcurrentDictionary<string, IntPtr> canonRealBody = [];

        // A stable, allocation-only identity for a canonical shared instantiation (no native handle access).
        private static string? CanonKey(MethodBase canonical)
        {
            try
            {
                var dt = canonical.DeclaringType;
                var dtKey = dt is null ? "" : (dt.AssemblyQualifiedName ?? dt.FullName ?? dt.Name);
                return dtKey + "|" + canonical.ToString();
            }
            catch
            {
                return null;
            }
        }

        // The recorded real code-start for the canon of closedSource, or zero.
        private static IntPtr GetRecordedCanonRealBody(MethodBase openSource, MethodBase closedSource)
        {
            try
            {
                var canonical = TryGetCanonicalSource(openSource, closedSource);
                if (canonical is null)
                {
                    return IntPtr.Zero;
                }
                var ck = CanonKey(canonical);
                return (ck is not null && canonRealBody.TryGetValue(ck, out var rb)) ? rb : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        // The canonical instantiation whose native code is the shared body, or null when canonicalization doesn't
        // apply. Only CoreCLR routes shared instantiations through instantiating stubs the walk must skip
        // Mono and .NET Framework stay on the existing path.
        private static MethodBase? TryGetCanonicalSource(MethodBase openSource, MethodBase closedSource)
        {
            if (PlatformDetection.Runtime != RuntimeKind.CoreCLR
                || !GenericHelper.MethodIsShared(closedSource)
                || canonType is not { } canon)
            {
                return null;
            }

            try
            {
                var dt = closedSource.DeclaringType;
                var declArgs = dt is { IsGenericType: true } ? dt.GetGenericArguments() : Type.EmptyTypes;
                var methodArgs = closedSource.IsGenericMethod ? closedSource.GetGenericArguments() : Type.EmptyTypes;

                var vector = new Type[declArgs.Length + methodArgs.Length];
                var n = 0;
                foreach (var t in declArgs)
                {
                    vector[n++] = Canonicalize(t, canon);
                }
                foreach (var t in methodArgs)
                {
                    vector[n++] = Canonicalize(t, canon);
                }

                return GenericInstantiator.CloseSource(openSource, vector);
            }
            catch (Exception ex)
            {
                MMDbgLog.Trace($"Could not construct canonical instantiation for {closedSource}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // Reference types collapse to __Canon;
        // value types are kept except a shared generic value type (e.g. a struct holding a reference type),
        // whose arguments are canonicalized recursively so it maps onto the same shared body the runtime uses.
        private static Type Canonicalize(Type t, Type canon)
        {
            if (!t.IsValueType)
            {
                return canon;
            }
            if (t.IsGenericType && GenericHelper.TypeIsShared(t))
            {
                var args = t.GetGenericArguments();
                for (var i = 0; i < args.Length; i++)
                {
                    args[i] = Canonicalize(args[i], canon);
                }
                return t.GetGenericTypeDefinition().MakeGenericType(args);
            }
            return t;
        }

        internal static IntPtr ResolveForDispatch(int bodyId, IntPtr contextPtr)
        {
            return bodiesById.TryGetValue(bodyId, out var body) ? body.Resolve(contextPtr) : IntPtr.Zero;
        }

        internal static IntPtr ResolveForDispatchThis(int bodyId, object self) => bodiesById.TryGetValue(bodyId, out var body) ? body.ResolveFromThisObject(self) : IntPtr.Zero;

        private static SharedDispatchTrampoline.Result BuildTopEntry(MethodBase closedSource, MethodInfo closedTarget, Delegate? orig, GenericContextReader.GenericContextKind kind)
        {
            // Fast path: a single replace hook on a ThisObject method can be jumped to directly, with no trampoline.
            if (orig is null && kind == GenericContextReader.GenericContextKind.ThisObject && !RequiresReturnBuffer(closedSource))
            {
                RuntimeHelpers.PrepareMethod(closedTarget.MethodHandle);
                return new SharedDispatchTrampoline.Result(PlatformTriple.Current.Runtime.GetMethodHandle(closedTarget).GetFunctionPointer(), default, closedTarget);
            }

            return SharedDispatchTrampoline.Build(closedSource, closedTarget, orig, kind);
        }

        private static void Compile(MethodBase closedSource)
        {
            if (PlatformDetection.Runtime == RuntimeKind.Framework)
            {
                // PlatformTriple.Compile throws "The given generic instantiation was invalid" here
                // and Runtime.GetMethodHandle crashes the net472 test runner, so pass the instantiation explicitly.
                var handle = closedSource.MethodHandle;
                var declArgs = closedSource.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : Type.EmptyTypes;
                var methodArgs = closedSource.IsGenericMethod ? closedSource.GetGenericArguments() : Type.EmptyTypes;

                if (declArgs.Length == 0 && methodArgs.Length == 0)
                {
                    RuntimeHelpers.PrepareMethod(handle);
                    return;
                }

                var all = new RuntimeTypeHandle[declArgs.Length + methodArgs.Length];
                var n = 0;
                foreach (var t in declArgs)
                {
                    all[n++] = t.TypeHandle;
                }
                foreach (var t in methodArgs)
                {
                    all[n++] = t.TypeHandle;
                }

                RuntimeHelpers.PrepareMethod(handle, all);
            }
            else
            {
                PlatformTriple.Current.Compile(closedSource);
            }
        }

        // Used to exclude the direct-function-pointer fast path, whose static target would otherwise swap `this` and the buffer.
        private static bool RequiresReturnBuffer(MethodBase m) => m is MethodInfo mi && GenericHelper.HasReturnBuffer(mi.ReturnType);

        // The Mono context-capture stub (GenericContextCaptureStub) is implemented for x86_64 and Arm64 and is only
        // needed on Mono for non-ThisObject kinds;
        private bool RequiresUnportedMonoCaptureStub
            => PlatformDetection.Runtime == RuntimeKind.Mono
               && _kind != GenericContextReader.GenericContextKind.ThisObject
               && PlatformDetection.Architecture is not (ArchitectureKind.x86_64 or ArchitectureKind.Arm64);

        private sealed class KeyChain : IDisposable
        {
            public sealed class Layer
            {
                public MethodInfo Target = null!;
                public bool HasOrig;
            }

            private readonly SharedBody body;
            public InstantiationKey Key { get; }
            private readonly MethodBase closedSource;
            internal MethodBase ClosedSource => closedSource;
            private readonly List<Layer> layers = [];

            private GenericOrigCloner? cloner;
            private Delegate? baseClone;
            // Keeps the orig-via-body trampoline alive while in use.
            private object? origViaBodyKeepAlive;

            private readonly List<GCHandle> curHandles = [];
            private readonly List<object> curKeepAlive = [];

            private volatile IntPtr entry;
            public IntPtr Entry => entry;

            public KeyChain(SharedBody body, InstantiationKey key, MethodBase closedSource)
            {
                this.body = body;
                Key = key;
                this.closedSource = closedSource;
            }

            public Layer AddLayer(MethodInfo target, bool hasOrig)
            {
                var layer = new Layer { Target = target, HasOrig = hasOrig };
                layers.Add(layer);
                Rebuild();
                return layer;
            }

            // Returns true if the chain was emptied
            public bool RemoveLayer(Layer layer)
            {
                layers.Remove(layer);
                if (layers.Count == 0)
                {
                    entry = IntPtr.Zero;
                    return true;
                }
                Rebuild();
                return false;
            }

            private void Rebuild()
            {
                RetireCurrent();

                var top = layers[layers.Count - 1];

                Delegate? origForTop = null;
                if (top.HasOrig)
                {
                    var origDelType = top.Target.GetParameters()[0].ParameterType;
                    EnsureBaseClone(origDelType);
                    origForTop = FoldBelowTop(origDelType);
                }

                var result = body.BuildTop(closedSource, top.Target, origForTop);
                if (result.OrigHandle.IsAllocated)
                {
                    curHandles.Add(result.OrigHandle);
                }
                if (result.KeepAlive is not null)
                {
                    curKeepAlive.Add(result.KeepAlive);
                }

                entry = result.Entry;
            }

            // Folds layers[0 .. count-2] into a single orig delegate whose bottom is the original clone
            private Delegate FoldBelowTop(Type origDelType)
            {
                var current = baseClone!;
                for (var i = 0; i < layers.Count - 1; i++)
                {
                    var layer = layers[i];
                    var step = SharedDispatchTrampoline.BuildManagedChainStep(layer.Target, layer.HasOrig ? current : null, origDelType);

                    if (step.InnerHandle.IsAllocated)
                    {
                        curHandles.Add(step.InnerHandle);
                    }
                    if (step.KeepAlive is not null)
                    {
                        curKeepAlive.Add(step.KeepAlive);
                    }
                    curKeepAlive.Add(step.Delegate);

                    current = step.Delegate;
                }
                return current;
            }

            private void EnsureBaseClone(Type origDelType)
            {
                if (baseClone is not null)
                {
                    return;
                }
                // .NET Core only; elsewhere the detour-free concrete clone below is used.
                if (body.TryBuildOrigViaBody(closedSource, Key, origDelType, out var viaBody, out var viaKeepAlive))
                {
                    baseClone = viaBody;
                    origViaBodyKeepAlive = viaKeepAlive;
                    return;
                }
                cloner = GenericOrigCloner.Create(closedSource);
                baseClone = cloner.CreateDelegate(origDelType);
            }

            // Dispatch is lock-free, so a thread can resolve an entry and be pre-empted before the calli; these
            // resources must not be freed while the body can still dispatch (a freed orig handle reads back null).
            // Defer to the body's retirement list, freed at Teardown when the detour is gone.
            // TODO: Better heuristic maybe?
            private void RetireCurrent()
            {
                if (curHandles.Count == 0 && curKeepAlive.Count == 0)
                {
                    return;
                }
                foreach (var h in curHandles)
                {
                    body.Retire(h, null);
                }
                foreach (var k in curKeepAlive)
                {
                    body.Retire(default, k);
                }
                curHandles.Clear();
                curKeepAlive.Clear();
            }

            public void Dispose()
            {
                entry = IntPtr.Zero;
                RetireCurrent();
                cloner?.Dispose();
                cloner = null;
                baseClone = null;
                origViaBodyKeepAlive = null;
            }
        }
    }
}

