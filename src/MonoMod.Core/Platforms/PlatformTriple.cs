using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface INeedsPlatformTripleInit {
        void Initialize(PlatformTriple factory);
        void PostInit();
    }
    public sealed class PlatformTriple {
        public static IRuntime CreateCurrentRuntime()
            => PlatformDetection.Runtime switch {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.CoreCLR => Runtimes.CoreCLRBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
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
        public static PlatformTriple Current => Helpers.GetOrInitWithLock(ref lazyCurrent, lazyCurrentLock, CreateCurrent);

        private static PlatformTriple CreateCurrent()
            => new(CreateCurrentArchitecture(), CreateCurrentSystem(), CreateCurrentRuntime());

        public PlatformTriple(IArchitecture architecture, ISystem system, IRuntime runtime) {
            Helpers.ThrowIfNull(architecture);
            Helpers.ThrowIfNull(system);
            Helpers.ThrowIfNull(runtime);

            Architecture = architecture;
            System = system;
            Runtime = runtime;

            // init the interface implementations
            InitIfNeeded(architecture, out var archIniter);
            InitIfNeeded(system, out var sysIniter);
            InitIfNeeded(runtime, out var rtIniter);
            archIniter?.PostInit();
            sysIniter?.PostInit();
            rtIniter?.PostInit();

            // eagerly initialize this so that the check functions get as much inlined as possible
            SupportedFeatures = new(Architecture.Features, System.Features, Runtime.Features);
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

        public bool DisableInliningIfPossible(MethodBase method) {
            if (SupportedFeatures.Has(RuntimeFeature.DisableInlining)) {
                Runtime.DisableInlining(method);
                return true;
            }

            return false;
        }

        public unsafe IntPtr GetNativeMethodBody(MethodBase method) {
            bool regenerated = false;

            var archMatchCollection = Architecture.KnownMethodThunks;

            ReloadFuncPtr:
            var entry = (nint) Runtime.GetMethodEntryPoint(method);

            do {
                var span = new ReadOnlySpan<byte>((void*) entry, archMatchCollection.MaxMinLength);

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

    }
}
