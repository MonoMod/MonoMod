#if NETSTANDARD1_X
using System;

namespace MonoMod.Utils {
    public sealed partial class DynamicMethodDefinition : IDisposable {

        private static void _InitCopier() {
        }

        private void _CopyMethodToDefinition() {
            throw new PlatformNotSupportedException();
        }

    }
}
#endif
