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
    public sealed class PlatformTriple {
        public static IRuntime CreateCurrentRuntime(ISystem system)
            => PlatformDetection.Runtime switch {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion, system),
                RuntimeKind.CoreCLR => Runtimes.CoreBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion, system),
                RuntimeKind.Mono => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Runtime kind {kind} not supported"),
            };

        public static IArchitecture CreateCurrentArchitecture(ISystem system)
            => PlatformDetection.Architecture switch {
                ArchitectureKind.x86 => new Architectures.x86Arch(),
                ArchitectureKind.x86_64 => new Architectures.x86_64Arch(system),
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

        private static PlatformTriple CreateCurrent() {
            var sys = CreateCurrentSystem();
            var arch = CreateCurrentArchitecture(sys);
            var runtime = CreateCurrentRuntime(sys);
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
        public NativeDetour? CreateNativeDetour(IntPtr from, IntPtr to, bool undoable = true, int detourMaxSize = -1) {
            Helpers.Assert(from != to, $"Cannot detour a method to itself! (from: {from}, to: {to})");

            var detourInfo = Architecture.ComputeDetourInfo(from, to, detourMaxSize);

            // detours are usually fairly small, so we'll stackalloc it
            Span<byte> detourData = stackalloc byte[detourInfo.Size];

            // get the detour bytes from the architecture
            var size = Architecture.GetDetourBytes(detourInfo, detourData, out var allocHandle);

            // these should be the same
            Helpers.DAssert(size == detourInfo.Size);

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

        public IntPtr GetNativeMethodBody(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.RequiresBodyThunkWalking)) {
                return GetNativeMethodBodyWalk(method);
            } else {
                return GetNativeMethodBodyDirect(method);
            }
        }

        private unsafe IntPtr GetNativeMethodBodyWalk(MethodBase method) {
            var regenerated = false;

            var archMatchCollection = Architecture.KnownMethodThunks;

            ReloadFuncPtr:
            var entry = (nint) Runtime.GetMethodEntryPoint(method);

            do {
                var readableLen = System.GetSizeOfReadableMemory(entry, archMatchCollection.MaxMinLength);

                // we still have to limit it like this because otherwise it'll scan and find *other* stubs
                // if we want to, we could scan for an arch-specific padding pattern and use that to limit instead
                var span = new ReadOnlySpan<byte>((void*) entry, Math.Min((int) readableLen, archMatchCollection.MaxMinLength));

                if (!archMatchCollection.TryFindMatch(span, out var addr, out var match, out var offset, out _))
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
