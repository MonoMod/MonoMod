using System;
using System.Reflection;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class FxCLR2Runtime : FxBaseRuntime {
        public override void DisableInlining(MethodBase method) {
            // the base classes don't specify RuntimeFeature.DisableInlining, so this should never be called
            throw new PlatformNotSupportedException();
        }
    }
}
