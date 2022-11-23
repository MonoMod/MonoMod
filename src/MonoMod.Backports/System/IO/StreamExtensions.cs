#if NET40_OR_GREATER || NETSTANDARD || NETCOREAPP
#define HAS_COPYTO
#endif

#if !HAS_COPYTO
using System.Buffers;
#endif

namespace System.IO {
    public static class StreamExtensions {
        public static void CopyTo(this Stream src, Stream destination) {
            if (src is null)
                ThrowHelper.ThrowArgumentNullException(nameof(src));

#if HAS_COPYTO
            src.CopyTo(destination);
#else
            CopyTo(src, destination, 81920);
#endif
        }
        public static void CopyTo(this Stream src, Stream destination, int bufferSize) {
            if (src is null)
                ThrowHelper.ThrowArgumentNullException(nameof(src));

#if HAS_COPYTO
            src.CopyTo(destination, bufferSize);
#else
            if (destination is null)
                ThrowHelper.ThrowArgumentNullException(nameof(destination));
            if (bufferSize < 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            var buf = ArrayPool<byte>.Shared.Rent(bufferSize);

            try {
                int read;
                do {
                    read = src.Read(buf, 0, buf.Length);
                    if (read > 0) {
                        destination.Write(buf, 0, read);
                    }
                } while (read > 0);
            } finally {
                ArrayPool<byte>.Shared.Return(buf, true);
            }
#endif
        }
    }
}
