using MonoMod.Backports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Utils {
    internal static class Buffer {
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static unsafe void MemoryCopy(void* source, void* destination, ulong destSize, ulong sourceSize) {
#if NETSTANDARD1_3_OR_GREATER || NET46_OR_GREATER || NETCOREAPP
            System.Buffer.MemoryCopy(source, destination, destSize, sourceSize);
#else
            MemoryCopy(new(source, (int) sourceSize), new(destination, (int) destSize));
#endif
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static unsafe void MemoryCopy(ReadOnlySpan<byte> source, Span<byte> destination) {
            source.CopyTo(destination);
        }
    }
}
