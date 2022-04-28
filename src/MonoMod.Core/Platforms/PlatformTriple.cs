using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms {
    public interface INeedsPlatformTripleInit {
        void Initialize(PlatformTriple triple);
        void PostInit();
    }
    public sealed class PlatformTriple {
        public static IRuntime CreateCurrentRuntime()
            => PlatformDetection.Runtime switch {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.CoreCLR => Runtimes.CoreBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.Mono => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Runtime kind {kind} not supported"),
            };

        public static IArchitecture CreateCurrentArchitecture()
            => PlatformDetection.Architecture switch {
                ArchitectureKind.x86 => throw new NotImplementedException(),
                ArchitectureKind.x86_64 => new Architectures.x86_64Arch(),
                ArchitectureKind.Arm => throw new NotImplementedException(),
                ArchitectureKind.Arm64 => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Architecture kind {kind} not supported"),
            };

        public static ISystem CreateCurrentSystem()
            => PlatformDetection.OS switch {
                OSKind.Posix => throw new NotImplementedException(),
                OSKind.Linux => throw new NotImplementedException(),
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

        public enum InitializationStatus {
            Uninitialized,
            InterfacesAvailable,
            InterfacesPartialInited,
            InterfacesInited,
            Complete,
        }

        public InitializationStatus Status { get; private set; }

        private static PlatformTriple CreateCurrent()
            => new(CreateCurrentArchitecture(), CreateCurrentSystem(), CreateCurrentRuntime());

        public PlatformTriple(IArchitecture architecture, ISystem system, IRuntime runtime) {
            Helpers.ThrowIfNull(architecture);
            Helpers.ThrowIfNull(system);
            Helpers.ThrowIfNull(runtime);

            Status = InitializationStatus.Uninitialized;

            Architecture = architecture;
            System = system;
            Runtime = runtime;

            Status = InitializationStatus.InterfacesAvailable;

            // init the interface implementations
            InitIfNeeded(architecture, out var archIniter);
            InitIfNeeded(system, out var sysIniter);
            InitIfNeeded(runtime, out var rtIniter);

            // eagerly initialize this so that the check functions get as much inlined as possible
            SupportedFeatures = new(Architecture.Features, System.Features, Runtime.Features);

            Status = InitializationStatus.InterfacesPartialInited;

            archIniter?.PostInit();
            sysIniter?.PostInit();
            rtIniter?.PostInit();

            Status = InitializationStatus.InterfacesInited;

            Abi = GetAbi();

            Status = InitializationStatus.Complete;
        }

        private Abi GetAbi() {
            var rtAbi = Runtime.Abi;

            Abi detected = default; // DO NOT REMOVE THIS = default! It's needed in Release.
            if (Helpers.IsDebug || rtAbi is null) {
                detected = AbiSelftest.DetectAbi(this);
            }

            if (rtAbi is not { } abi)
                return detected;

            Helpers.DAssert(abi.ReturnsReturnBuffer == detected.ReturnsReturnBuffer, 
                $"Known and detected ABI provide different values for ReturnsReturnBuffer. " +
                $"known = {abi.ReturnsReturnBuffer}, detected = {detected.ReturnsReturnBuffer}");
            Helpers.DAssert(FilterArgOrder(abi.ArgumentOrder).SequenceEqual(FilterArgOrder(detected.ArgumentOrder)),
                $"Known and detected ABI provide different argument orders. " +
                $"known = {{ {JoinArgOrder(abi.ArgumentOrder)} }}, detected = {{ {JoinArgOrder(abi.ArgumentOrder)} }}");

#pragma warning disable CS0162 // Unreachable code detected
            if (Helpers.IsDebug) {
                var classifier = new DebugClassifier(abi, detected);
                return new(abi.ArgumentOrder, classifier.Classifier, abi.ReturnsReturnBuffer);
            } else {
                return abi;
            }
#pragma warning restore CS0162 // Unreachable code detected
        }
        private static IEnumerable<SpecialArgumentKind> FilterArgOrder(ReadOnlyMemory<SpecialArgumentKind> order) {
            var seg = GetOrCreateArray(order);
            Helpers.DAssert(seg.Array is not null);
            for (var i = seg.Offset; i < seg.Offset + seg.Count; i++) {
                var val = seg.Array[i];
                // TODO: selftest generic context pointer location
                if (val == SpecialArgumentKind.GenericContext)
                    continue;
                yield return val;
            }
        }

        private static string JoinArgOrder(ReadOnlyMemory<SpecialArgumentKind> order)
            => string.Join(", ", FilterArgOrder(order).Select(a => a.ToString()).ToArray());

        private static ArraySegment<T> GetOrCreateArray<T>(ReadOnlyMemory<T> mem) {
            if (global::System.Runtime.InteropServices.MemoryMarshal.TryGetArray(mem, out var seg)) {
                return seg;
            } else {
                return new(mem.ToArray());
            }
        }

        private sealed class DebugClassifier {
            public readonly Abi Runtime;
            public readonly Abi Detected;

            public Classifier Classifier { get; }

            public DebugClassifier(Abi rt, Abi det) {
                Runtime = rt;
                Detected = det;

                Classifier = Classify;
            }

            private TypeClassification Classify(Type type, bool isRet) {
                var rtResult = Runtime.Classifier(type, isRet);
                var detResult = Detected.Classifier(type, isRet);

                Helpers.Assert(rtResult == detResult,
                    $"Known ABI and detected ABI returned different classifications for {type.AssemblyQualifiedName} ({(isRet ? "ret" : "arg")}). " +
                    $"known = {rtResult}, detected = {detResult}");

                return rtResult;
            }
        }

        private void InitIfNeeded(object obj, out INeedsPlatformTripleInit? initer) {
            if (obj is INeedsPlatformTripleInit init) {
                initer = init;
                init.Initialize(this);
            } else {
                initer = null;
            }
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
            Helpers.ThrowIfNull(method);

            if (method.IsGenericMethodDefinition) {
                throw new ArgumentException("Cannot prepare generic method definition", nameof(method));
            }

            method = GetIdentifiable(method);
            var handle = Runtime.GetMethodHandle(method);

            if (method.IsGenericMethod) {
                // we need to get the handles of the type args too
                var typeArgs = method.GetGenericArguments();
                var argHandles = new RuntimeTypeHandle[typeArgs.Length];
                for (int i = 0; i < typeArgs.Length; i++)
                    argHandles[i] = typeArgs[i].TypeHandle;

                RuntimeHelpers.PrepareMethod(handle, argHandles);
            } else {
                // or we can just call the normal PrepareMethod
                RuntimeHelpers.PrepareMethod(handle);
            }
        }

        public MethodBase GetIdentifiable(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresMethodIdentification)) {
                // see the comment in PinMethodIfNeeded
                return Runtime.GetIdentifiable(method);
            }

            // if the runtime doesn't require method identification, we just return the provided method implementation.
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
        public NativeDetour? CreateNativeDetour(IntPtr from, IntPtr to, bool undoable = true) {
            var detourInfo = Architecture.ComputeDetourInfo(from, to);

            // detours are usually fairly small, so we'll stackalloc it
            Span<byte> detourData = stackalloc byte[detourInfo.Size];

            // get the detour bytes from the architecture
            var size = Architecture.GetDetourBytes(detourInfo, detourData, out var allocHandle);

            // these should be the same
            Helpers.Assert(size == detourInfo.Size);

            // allocate a backup if needed
            var backup = undoable ? new byte[detourInfo.Size] : null;

            // now we can apply the detour through the system
            System.PatchData(PatchTargetKind.Executable, from, detourData, backup);

            // and now we just create the NativeDetour object, if its supposed to be undoable
            if (undoable) {
                // if we're undoable, pass the allocHandle to the NativeDetour
                return new NativeDetour(this, from, to, backup, allocHandle);
            } else {
                // otherwise, create a GCHandle to it and throw it away
                _ = GCHandle.Alloc(allocHandle);
                allocHandle = null;
                return null;
            }
        }

        public IntPtr GetNativeMethodBody(MethodBase method, bool followThunks = true) {
            if (followThunks && SupportedFeatures.Has(RuntimeFeature.RequiresBodyThunkWalking)) {
                return GetNativeMethodBodyWalk(method);
            } else {
                return GetNativeMethodBodyDirect(method);
            }
        }

        private unsafe IntPtr GetNativeMethodBodyWalk(MethodBase method) {
            bool regenerated = false;

            var archMatchCollection = Architecture.KnownMethodThunks;

            ReloadFuncPtr:
            var entry = (nint) Runtime.GetMethodEntryPoint(method);

            do {
                var readableLen = System.GetSizeOfReadableMemory(entry, archMatchCollection.MaxMinLength);

                // we still have to limit it like this because otherwise it'll scan and find *other* stubs
                // if we want to, we could scan for an arch-specific padding pattern and use that to limit instead
                var span = new ReadOnlySpan<byte>((void*) entry, Math.Min((int) readableLen, archMatchCollection.MaxMinLength));

                if (!archMatchCollection.TryFindMatch(span, out var addr, out var match, out var offset, out var length))
                    break;

                var lastEntry = entry;

                var meaning = match.AddressMeaning;
                if (meaning.Kind.IsPrecodeFixup() && !regenerated) {
                    var precode = meaning.ProcessAddress(entry, offset, addr);
                    MMDbgLog.Log($"Method thunk reset; regenerating (PrecodeFixupThunk: 0x{precode:X16})");
                    Prepare(method);
                    goto ReloadFuncPtr;
                } else {
                    entry = meaning.ProcessAddress(entry, offset, addr);
                }

                entry = NotThePreStub(lastEntry, entry);
            } while (true);

            return entry;
        }

        private unsafe IntPtr GetNativeMethodBodyDirect(MethodBase method) {
            return Runtime.GetMethodEntryPoint(method);
        }

        private IntPtr ThePreStub = IntPtr.Zero;

        private IntPtr NotThePreStub(IntPtr ptrGot, IntPtr ptrParsed) {
            if (ThePreStub == IntPtr.Zero) {
                ThePreStub = (IntPtr) (-2);

                // FIXME: Find a better less likely called NGEN'd candidate that points to ThePreStub.
                // This was "found" by tModLoader.
                // Can be missing in .NET 5.0 outside of Windows for some reason.
                var mi = typeof(System.Net.HttpWebRequest).Assembly
                    .GetType("System.Net.Connection")
                    ?.GetMethod("SubmitRequest", BindingFlags.NonPublic | BindingFlags.Instance);

                if (mi != null) {
                    ThePreStub = GetNativeMethodBody(mi);
                    MMDbgLog.Log($"ThePreStub: 0x{(long) ThePreStub:X16}");
                } else if (PlatformDetection.OS.Is(OSKind.Windows)) {
                    // FIXME: This should be -1 (always return ptrGot) on all plats, but SubmitRequest is Windows-only?
                    ThePreStub = (IntPtr) (-1);
                }
            }

            return (ptrParsed == ThePreStub /*|| ThePreStub == (IntPtr) (-1)*/) ? ptrGot : ptrParsed;
        }

        public MethodBase GetRealDetourTarget(MethodBase from, MethodBase to) {
            Helpers.ThrowIfNull(from);
            Helpers.ThrowIfNull(to);

            to = GetIdentifiable(to);

            // TODO: check that from and to are actually argument- and return-compatible
            // this check would ensure that to is only non-static when that makes sense

            if (from is MethodInfo fromInfo &&
                to is MethodInfo toInfo &&
                !fromInfo.IsStatic && to.IsStatic) {
                var retType = fromInfo.ReturnType;
                // if from has `this` and to doesn't, then we need to fix up the abi
                var returnClass = Abi.Classify(retType, true);

                // only if the return class is PointerToMemory do we need to do something
                if (returnClass == TypeClassification.PointerToMemory) {
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
                        $"Glue:AbiFixup<{from.GetID(simple: true)},{to.GetID(simple: true)}>",
                        newRetType, argTypes.ToArray()
                    )) {
                        dmd.Definition.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining |
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
