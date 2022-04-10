using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms.Runtime {
    internal abstract class FxBaseRuntime : FxCoreCLRBaseRuntime {
        public override RuntimeKind Target => RuntimeKind.Framework;
    }
}
