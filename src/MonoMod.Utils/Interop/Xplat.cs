using System;
using System.Buffers;
using System.Text;

namespace MonoMod.Utils.Interop
{
    internal static class Xplat
    {
        internal static byte[]? MarshalToUtf8(string? str)
        {
            if (str is null)
                return null;

            var len = Encoding.UTF8.GetByteCount(str);
            var arr = ArrayPool<byte>.Shared.Rent(len + 1);
            arr.AsSpan().Clear();
            var encoded = Encoding.UTF8.GetBytes(str, 0, str.Length, arr, 0);
            Helpers.DAssert(len == encoded);
            return arr;
        }

        internal static void FreeMarshalledArray(byte[]? arr)
        {
            if (arr is null)
                return;
            ArrayPool<byte>.Shared.Return(arr);
        }
    }
}
