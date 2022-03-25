using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Utils {
    internal static class Buffer {
        [MethodImpl(Helpers.AggressiveInlining)]
        public static unsafe void MemoryCopy(void* source, void* destination, ulong destSize, ulong sourceSize) {
#if NETSTANDARD1_3_OR_GREATER || NET46_OR_GREATER || NETCOREAPP
            System.Buffer.MemoryCopy(source, destination, destSize, sourceSize);
#else
            // TODO: implement a fairly efficient memcopy for net35 and net451
            throw new NotImplementedException();
#endif
        }

        public static unsafe bool MemCmp(SimpleByteSpan a, SimpleByteSpan b) {
            // TODO: implement
            throw new NotImplementedException();
        }
    }
}
