using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MonoMod.Core.Platforms.Runtimes {
    internal abstract class CoreBaseRuntime : FxCoreBaseRuntime, INeedsPlatformTripleInit {

        public static CoreBaseRuntime CreateForVersion(Version version) {

            switch (version.Major) {
                case 2:
                case 4:
                    // .NET Core 2.1 (it actually only seems to give a major version of 4, but 2 is here for safety)
                    // TODO:
                    break;

                case 3:
                    // .NET Core 3.x
                    return version.Minor switch {
                        0 => new Core30Runtime(),
                        1 => new Core31Runtime(),
                        _ => throw new PlatformNotSupportedException($"Unknown .NET Core 3.x minor version {version.Minor}"),
                    };

                case 5:
                    // .NET 5.0.x
                    return new Core50Runtime();

                case 6:
                    // .NET 6.0.x
                    return new Core60Runtime();

                /*
                case 7:
                    // .NET 7.0.x
                    // TODO:
                    break;
                */

                // currently, we need to manually add support for new versions.
                // TODO: possibly fall back to a JIT GUID check if we can?

                default: throw new PlatformNotSupportedException($"CoreCLR version {version} is not supported");
            }

            throw new NotImplementedException();
        }

        public override RuntimeKind Target => RuntimeKind.CoreCLR;

        private PlatformTriple platformTriple;

        protected CoreBaseRuntime() {
            platformTriple = null!;
        }

        void INeedsPlatformTripleInit.Initialize(PlatformTriple triple) {
            platformTriple = triple;
        }

        void INeedsPlatformTripleInit.PostInit() {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64 &&
                platformTriple.System.DefaultAbi is { } abi) {
                AbiCore = AbiForCoreFx45X64(abi);
            }

            InstallJitHook(JitObject);
        }

        protected virtual string GetClrJitPath() {
            var currentProc = Process.GetCurrentProcess();
            var clrjitModule = currentProc.Modules.Cast<ProcessModule>()
                .FirstOrDefault(m => m.FileName is not null && Path.GetFileNameWithoutExtension(m.FileName).EndsWith("clrjit", StringComparison.Ordinal));
            if (clrjitModule is null)
                throw new PlatformNotSupportedException("Could not locate clrjit library");

            Helpers.DAssert(clrjitModule.FileName is not null);
            return clrjitModule.FileName;
        }

        private IntPtr? lazyJitObject;
        protected IntPtr JitObject => lazyJitObject ??= GetJitObject();

        private unsafe IntPtr GetJitObject() {
            var path = GetClrJitPath();

            if (!DynDll.TryOpenLibrary(path, out var clrjit, true))
                throw new PlatformNotSupportedException("Could not open clrjit library");

            try {
                return ((delegate* unmanaged[Stdcall]<IntPtr>) DynDll.GetFunction(clrjit, "getJit"))();
            } catch {
                DynDll.CloseLibrary(clrjit);
                throw;
            }
        }

        protected PlatformTriple Triple => platformTriple;
        protected abstract void InstallJitHook(IntPtr jit);
    }
}
