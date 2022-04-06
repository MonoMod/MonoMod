using MonoMod.Backports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Utils {
    internal static class Helpers {
        public static void Swap<T>(ref T a, ref T b) => (b, a) = (a, b);

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static ulong NumericValue<T>(T value) where T : struct, Enum {
            ulong result = 0;
            Unsafe.CopyBlock(ref Unsafe.As<ulong, byte>(ref result), ref Unsafe.As<T, byte>(ref value), (uint)Unsafe.SizeOf<T>());
            return result;
        }
    }
}
