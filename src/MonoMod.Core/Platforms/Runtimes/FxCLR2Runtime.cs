using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class FxCLR2Runtime : FxBaseRuntime {
        public override IAbi Abi => throw new NotImplementedException();

        public override void DisableInlining(MethodBase method) {
            // the base classes don't specify RuntimeFeature.DisableInlining, so this should never be called
            throw new PlatformNotSupportedException();
        }
    }
}
