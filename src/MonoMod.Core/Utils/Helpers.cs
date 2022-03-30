using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Utils {
    internal static class Helpers {
        public static void Swap<T>(ref T a, ref T b) => (b, a) = (a, b);
    }
}
