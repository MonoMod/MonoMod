using MonoMod.Core.Utils;
using System;
using System.Reflection;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class FxCLR4Runtime : FxBaseRuntime {

        private ISystem system;

        public FxCLR4Runtime(ISystem system) {
            this.system = system;

            // the only place I could find the actual version number of 4.5 (without just testing myself on a Win7 VM) is here:
            // https://stackoverflow.com/a/11512846
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64 &&
                (PlatformDetection.RuntimeVersion.Revision >= 17379 ||
                PlatformDetection.RuntimeVersion.Minor >= 5) &&
                system.DefaultAbi is { } abi) {
                AbiCore = AbiForCoreFx45X64(abi);
            }
        }

        /*public override void DisableInlining(MethodBase method) {
            // the base classes don't specify RuntimeFeature.DisableInlining, so this should never be called
            throw new PlatformNotSupportedException();
        }*/
    }
}
