using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class FxCLR4Runtime : FxBaseRuntime, INeedsPlatformTripleInit {

        private PlatformTriple platformTriple;

        public FxCLR4Runtime() {
            platformTriple = null!;
        }

        void INeedsPlatformTripleInit.Initialize(PlatformTriple triple) {
            platformTriple = triple;
        }

        void INeedsPlatformTripleInit.PostInit() {
            // the only place I could find the actual version number of 4.5 (without just testing myself on a Win7 VM) is here:
            // https://stackoverflow.com/a/11512846
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64 &&
                PlatformDetection.RuntimeVersion.Revision >= 17379 &&
                platformTriple.System.DefaultAbi is { } abi) {
                AbiCore = AbiForCoreFx45X64(abi);
            } else if (AbiCore is null) {
                // TODO: run selftests to detect
                throw new PlatformNotSupportedException();
            }
        }

        public override void DisableInlining(MethodBase method) {
            // the base classes don't specify RuntimeFeature.DisableInlining, so this should never be called
            throw new PlatformNotSupportedException();
        }
    }
}
