using MonoMod.Backports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Utils {
    public static class Extensions {
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool Has<T>(this T @enum, T value) where T : struct, Enum {
            var flgsVal = Helpers.NumericValue(value);
            return (Helpers.NumericValue(@enum) & flgsVal) == flgsVal;
        }
    }
}
