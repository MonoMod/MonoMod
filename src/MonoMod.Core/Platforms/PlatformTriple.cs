using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Core.Utils;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Platforms {
    public sealed class PlatformTriple {
        public static IRuntime CreateCurrentRuntime(ISystem system, IArchitecture arch)
            => PlatformDetection.Runtime switch {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion, system),
                RuntimeKind.CoreCLR => Runtimes.CoreBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion, system, arch),
                RuntimeKind.Mono => new Runtimes.MonoRuntime(system),
                var kind => throw new PlatformNotSupportedException($"Runtime kind {kind} not supported"),
            };

        public static IArchitecture CreateCurrentArchitecture(ISystem system)
            => PlatformDetection.Architecture switch {
                ArchitectureKind.x86 => new Architectures.x86Arch(system),
                ArchitectureKind.x86_64 => new Architectures.x86_64Arch(system),
                ArchitectureKind.Arm => throw new NotImplementedException(),
                ArchitectureKind.Arm64 => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Architecture kind {kind} not supported"),
            };

        public static ISystem CreateCurrentSystem()
            => PlatformDetection.OS switch {
                OSKind.Posix => throw new NotImplementedException(),
                OSKind.Linux => new Systems.LinuxSystem(),
                OSKind.Android => throw new NotImplementedException(),
                OSKind.OSX => throw new NotImplementedException(),
                OSKind.IOS => throw new NotImplementedException(),
                OSKind.BSD => throw new NotImplementedException(),
                OSKind.Windows or OSKind.Wine => new Systems.WindowsSystem(),
                var kind => throw new PlatformNotSupportedException($"OS kind {kind} not supported"),
            };

        public IArchitecture Architecture { get; }
        public ISystem System { get; }
        public IRuntime Runtime { get; }

        private static object lazyCurrentLock = new();
        private static PlatformTriple? lazyCurrent;
        public static unsafe PlatformTriple Current => Helpers.GetOrInitWithLock(ref lazyCurrent, lazyCurrentLock, &CreateCurrent);

        private static PlatformTriple CreateCurrent() {
            var sys = CreateCurrentSystem();
            var arch = CreateCurrentArchitecture(sys);
            var runtime = CreateCurrentRuntime(sys, arch);
            return new(arch, sys, runtime);
        }

        public PlatformTriple(IArchitecture architecture, ISystem system, IRuntime runtime) {
            Helpers.ThrowIfArgumentNull(architecture);
            Helpers.ThrowIfArgumentNull(system);
            Helpers.ThrowIfArgumentNull(runtime);

            Architecture = architecture;
            System = system;
            Runtime = runtime;

            // eagerly initialize this so that the check functions get as much inlined as possible
            SupportedFeatures = new(Architecture.Features, System.Features, Runtime.Features);

            InitIfNeeded(Architecture);
            InitIfNeeded(System);
            InitIfNeeded(Runtime);

            Abi = Runtime.Abi;
        }

        private static void InitIfNeeded(object obj) {
            (obj as IInitialize)?.Initialize();
        }

        public (ArchitectureKind Arch, OSKind OS, RuntimeKind Runtime) HostTriple => (Architecture.Target, System.Target, Runtime.Target);

        public FeatureFlags SupportedFeatures { get; }

        public Abi Abi { get; }

        /// <summary>
        /// Prepares <paramref name="method"/> by calling <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>, this method handles generic instantiations.
        /// In order to do this, however, it has to perform a fair bit of reflection on invocation. Avoid calling it multiple times
        /// for the same method, if possible.
        /// </remarks>
        /// <param name="method">The method to prepare.</param>
        public void Prepare(MethodBase method) {
            Helpers.ThrowIfArgumentNull(method);

            if (method.IsGenericMethodDefinition) {
                throw new ArgumentException("Cannot prepare generic method definition", nameof(method));
            }

            method = GetIdentifiable(method);
            var handle = Runtime.GetMethodHandle(method);

            if (method.IsGenericMethod) {
                // we need to get the handles of the type args too
                var typeArgs = method.GetGenericArguments();
                var argHandles = new RuntimeTypeHandle[typeArgs.Length];
                for (var i = 0; i < typeArgs.Length; i++)
                    argHandles[i] = typeArgs[i].TypeHandle;

                RuntimeHelpers.PrepareMethod(handle, argHandles);
            } else {
                // or we can just call the normal PrepareMethod
                RuntimeHelpers.PrepareMethod(handle);
            }
        }

        public MethodBase GetIdentifiable(MethodBase method) {
            Helpers.ThrowIfArgumentNull(method);

            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodIdentification)) {
                // see the comment in PinMethodIfNeeded
                method = Runtime.GetIdentifiable(method);
            }

            // because the .NET reflcetion APIs are really bad, two MethodBases may not compare equal if they represent the same method
            // *but were gotten through different means*. Because MemberInfo.ReflectedType exists.
            // In order to fix this, when getting an identifiable method, we make sure to correct it, by retrieving it directly from its declaring type (or module, as it may be)
            if (method.ReflectedType != method.DeclaringType) {
                var parameters = method.GetParameters();
                var paramTypes = new Type[parameters.Length];
                for (var i = 0; i < parameters.Length; i++) {
                    paramTypes[i] = parameters[i].ParameterType;
                }

                if (method.DeclaringType is null) {
                    // the method lives on the module, get it from there
                    var got = method.Module.GetMethod(method.Name, (BindingFlags) (-1), null, method.CallingConvention, paramTypes, null);
                    Helpers.Assert(got is not null, $"orig: {method}, module: {method.Module}");
                    method = got;
                } else {
                    // the method has a declaring type, get it there
                    if (method.IsConstructor) {
                        var got = method.DeclaringType.GetConstructor((BindingFlags) (-1), null, method.CallingConvention, paramTypes, null);
                        Helpers.Assert(got is not null, $"orig: {method}");
                        method = got;
                    } else {
                        var got = method.DeclaringType.GetMethod(method.Name, (BindingFlags) (-1), null, method.CallingConvention, paramTypes, null);
                        Helpers.Assert(got is not null, $"orig: {method}");
                        method = got;
                    }
                }
            }

            return method;
        }

        public IDisposable? PinMethodIfNeeded(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodPinning)) {
                // only make the interface call if it's needed, because interface dispatches are slow
                return Runtime.PinMethodIfNeeded(method);
            }

            // otherwise, always return
            return null;
        }

        public bool TryDisableInlining(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.DisableInlining)) {
                Runtime.DisableInlining(method);
                return true;
            }

            return false;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "allocHandle is correctly transferred around, as needed")]
        public SimpleNativeDetour CreateSimpleDetour(IntPtr from, IntPtr to, int detourMaxSize = -1, IntPtr fromRw = default) {
            if (fromRw == default) {
                fromRw = from;
            }
            Helpers.Assert(from != to, $"Cannot detour a method to itself! (from: {from}, to: {to})");

            MMDbgLog.Trace($"Creating simple detour 0x{from:x16} => 0x{to:x16}");

            var detourInfo = Architecture.ComputeDetourInfo(from, to, detourMaxSize);

            // detours are usually fairly small, so we'll stackalloc it
            Span<byte> detourData = stackalloc byte[detourInfo.Size];

            // get the detour bytes from the architecture
            var size = Architecture.GetDetourBytes(detourInfo, detourData, out var allocHandle);

            // these should be the same
            Helpers.DAssert(size == detourInfo.Size);

            // allocate a backup
            var backup = new byte[detourInfo.Size];

            // now we can apply the detour through the system
            System.PatchData(PatchTargetKind.Executable, fromRw, detourData, backup);

            // and now we just create the NativeDetour object
            return new SimpleNativeDetour(this, detourInfo, backup, allocHandle);
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "allocHandle is correctly transferred around, as needed")]
        public NativeDetour CreateNativeDetour(IntPtr from, IntPtr to, int detourMaxSize = -1, IntPtr fromRw = default) {
            if (fromRw == default) {
                fromRw = from;
            }
            Helpers.Assert(from != to, $"Cannot detour a method to itself! (from: {from}, to: {to})");

            MMDbgLog.Trace($"Creating simple detour 0x{from:x16} => 0x{to:x16}");

            var detourInfo = Architecture.ComputeDetourInfo(from, to, detourMaxSize);

            // detours are usually fairly small, so we'll stackalloc it
            Span<byte> detourData = stackalloc byte[detourInfo.Size];

            // get the detour bytes from the architecture
            var size = Architecture.GetDetourBytes(detourInfo, detourData, out var allocHandle);

            // these should be the same
            Helpers.DAssert(size == detourInfo.Size);
            
            // now that we have the detour size, we'll try to allocate an alternate entry point
            IntPtr altEntry = IntPtr.Zero;
            IDisposable? altHandle = null;
            if (SupportedFeatures.Has(ArchitectureFeature.CreateAltEntryPoint)) {
                altEntry = Architecture.AltEntryFactory.CreateAlternateEntrypoint(from, size, out altHandle);
            } else {
                MMDbgLog.Warning($"Cannot create alternate entry point for native detour (from: {from:x16}, to: {to:x16}");
            }

            // allocate a backup
            var backup = new byte[detourInfo.Size];

            // now we can apply the detour through the system
            System.PatchData(PatchTargetKind.Executable, fromRw, detourData, backup);

            // and now we just create the NativeDetour object
            return new NativeDetour(this, detourInfo, backup, allocHandle, altEntry, altHandle);
        }
        
        public IntPtr GetNativeMethodBody(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresBodyThunkWalking)) {
                return GetNativeMethodBodyWalk(method, reloadPtr: true);
            } else {
                return GetNativeMethodBodyDirect(method);
            }
        }

        private unsafe IntPtr GetNativeMethodBodyWalk(MethodBase method, bool reloadPtr) {
            var regenerated = false;
            var didPrepareLastIter = false;

            var archMatchCollection = Architecture.KnownMethodThunks;

            MMDbgLog.Trace($"Performing method body walk for {method}");

            nint prevEntry = -1;

            ReloadFuncPtr:
            var entry = (nint) Runtime.GetMethodEntryPoint(method);
            MMDbgLog.Trace($"Starting entry point = 0x{entry:x16}");
            do {
                if (!didPrepareLastIter && prevEntry == entry) {
                    // we're in a loop, break out
                    break;
                }
                prevEntry = entry;

                var readableLen = System.GetSizeOfReadableMemory(entry, archMatchCollection.MaxMinLength);
                if (readableLen <= 0) {
                    MMDbgLog.Warning($"Got zero or negative readable length {readableLen} at 0x{entry:x16}");
                }

                // we still have to limit it like this because otherwise it'll scan and find *other* stubs
                // if we want to, we could scan for an arch-specific padding pattern and use that to limit instead
                var span = new ReadOnlySpan<byte>((void*) entry, Math.Min((int) readableLen, archMatchCollection.MaxMinLength));

                // TODO: be more limiting with which patterns can be scanned forward and which cannot
                if (!archMatchCollection.TryFindMatch(span, out var addr, out var match, out var offset, out _))
                    break;

                var lastEntry = entry;

                didPrepareLastIter = false;

                var meaning = match.AddressMeaning;
                MMDbgLog.Trace($"Matched thunk with {meaning} at 0x{entry:x16} (addr: 0x{addr:x8}, offset: {offset})");
                if (meaning.Kind.IsPrecodeFixup() && !regenerated) {
                    var precode = meaning.ProcessAddress(entry, offset, addr);
                    if (reloadPtr) {
                        MMDbgLog.Trace($"Method thunk reset; regenerating (PrecodeFixupThunk: 0x{precode:X16})");
                        Prepare(method);
                        didPrepareLastIter = true;
                        //regenerated = true;
                        goto ReloadFuncPtr;
                    } else {
                        entry = precode;
                    }
                } else {
                    entry = meaning.ProcessAddress(entry, offset, addr);
                }
                MMDbgLog.Trace($"Got next entry point 0x{entry:x16}");

                entry = NotThePreStub(lastEntry, entry, out var wasPreStub);
                if (wasPreStub && reloadPtr) {
                    MMDbgLog.Trace("Matched ThePreStub");
                    Prepare(method);
                    //regenerated = true;
                    goto ReloadFuncPtr;
                }
            } while (true);

            return entry;
        }

        private unsafe IntPtr GetNativeMethodBodyDirect(MethodBase method) {
            return Runtime.GetMethodEntryPoint(method);
        }

        private IntPtr ThePreStub = IntPtr.Zero;

        // TODO: make this something actually runtime-dependent
        private IntPtr NotThePreStub(IntPtr ptrGot, IntPtr ptrParsed, out bool wasPreStub) {
            if (ThePreStub == IntPtr.Zero) {
                ThePreStub = (IntPtr) (-2);

                // FIXME: Find a better less likely called NGEN'd candidate that points to ThePreStub.
                // This was "found" by tModLoader.
                // Can be missing in .NET 5.0 outside of Windows for some reason.

                // Instead of using any specific method on System.Net.Connection, we just check all of them, as (hopefully) most aren't called by this point
                var pre = typeof(System.Net.HttpWebRequest).Assembly
                    .GetType("System.Net.Connection")
                    ?.GetMethods()
                    .GroupBy(m => GetNativeMethodBodyWalk(m, reloadPtr: false))
                    .First(g => g.Count() > 1)
                    .Key ?? (nint) (-1);

                ThePreStub = pre;
                MMDbgLog.Trace($"ThePreStub: 0x{ThePreStub:X16}");
            }

            wasPreStub = ptrParsed == ThePreStub /*|| ThePreStub == (IntPtr) (-1)*/;

            return wasPreStub ? ptrGot : ptrParsed;
        }

        public MethodBase GetRealDetourTarget(MethodBase from, MethodBase to) {
            Helpers.ThrowIfArgumentNull(from);
            Helpers.ThrowIfArgumentNull(to);

            to = GetIdentifiable(to);

            // TODO: check that from and to are actually argument- and return-compatible
            // this check would ensure that to is only non-static when that makes sense

            if (from is MethodInfo fromInfo &&
                to is MethodInfo toInfo &&
                !fromInfo.IsStatic && to.IsStatic) {
                var retType = fromInfo.ReturnType;
                // if from has `this` and to doesn't, then we need to fix up the abi
                var returnClass = Abi.Classify(retType, true);

                // only if the return class is ByRef do we need to do something
                // TODO: perform better decisions based on the ABI argument order and return class
                if (returnClass == TypeClassification.ByReference) {
                    var thisType = from.GetThisParamType();
                    var retPtrType = retType.MakeByRefType();

                    var newRetType = Abi.ReturnsReturnBuffer ? retPtrType : typeof(void);

                    int thisPos = -1, retBufPos = -1, argOffset = -1;

                    var paramList = from.GetParameters();

                    var argTypes = new List<Type>();
                    var order = Abi.ArgumentOrder.Span;
                    for (var i = 0; i < order.Length; i++) {
                        var kind = order[i];
                        
                        if (kind == SpecialArgumentKind.ThisPointer) {
                            thisPos = argTypes.Count;
                            argTypes.Add(thisType);
                        } else if (kind == SpecialArgumentKind.ReturnBuffer) {
                            retBufPos = argTypes.Count;
                            argTypes.Add(retPtrType);
                        } else if (kind == SpecialArgumentKind.UserArguments) {
                            argOffset = argTypes.Count;
                            argTypes.AddRange(paramList.Select(p => p.ParameterType));
                        }

                        // TODO: somehow handle generic context parameters
                        // or more likely, just ignore it for this and do generics elsewhere
                    }

                    Helpers.DAssert(thisPos >= 0);
                    Helpers.DAssert(retBufPos >= 0);
                    Helpers.DAssert(argOffset >= 0);

                    using (var dmd = new DynamicMethodDefinition(
                        DebugFormatter.Format($"Glue:AbiFixup<{from},{to}>"),
                        newRetType, argTypes.ToArray()
                    )) {
                        // TODO: make DMD apply atributes to the generated DynamicMethod, when possible
                        dmd.Definition!.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining |
                            (Mono.Cecil.MethodImplAttributes) (int) MethodImplOptionsEx.AggressiveOptimization;

                        var il = dmd.GetILProcessor();

                        // load return buffer
                        il.Emit(OpCodes.Ldarg, retBufPos);

                        // load thisptr
                        il.Emit(OpCodes.Ldarg, thisPos);

                        // load user arguments
                        for (var i = 0; i < paramList.Length; i++) {
                            il.Emit(OpCodes.Ldarg, i + argOffset);
                        }

                        // call the target method
                        il.Emit(OpCodes.Call, il.Body.Method.Module.ImportReference(to));

                        // store the returned object
                        il.Emit(OpCodes.Stobj, il.Body.Method.Module.ImportReference(retType));

                        // if we need to return the pointer, do that
                        if (Abi.ReturnsReturnBuffer) {
                            il.Emit(OpCodes.Ldarg, retBufPos);
                        }

                        // then we're done
                        il.Emit(OpCodes.Ret);

                        return dmd.Generate();
                    }
                }
            }

            return to;
        }
    }
}
