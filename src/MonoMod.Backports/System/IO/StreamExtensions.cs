#if NET40_OR_GREATER || NETSTANDARD || NETCOREAPP
#define HAS_COPYTO
#endif

#if !HAS_COPYTO
using System.Buffers;
#endif

namespace System.IO {
    public static class StreamExtensions {
        public static void CopyTo(this Stream src, Stream destination) {
            ThrowHelper.ThrowIfArgumentNull(src, nameof(src));

#if HAS_COPYTO
            src.CopyTo(destination);
#else
            CopyTo(src, destination, 81920);
#endif
        }
        public static void CopyTo(this Stream src, Stream destination, int bufferSize) {
            ThrowHelper.ThrowIfArgumentNull(src, nameof(src));

#if HAS_COPYTO
            src.CopyTo(destination, bufferSize);
#else
            ThrowHelper.ThrowIfArgumentNull(destination, nameof(destination));
            if (bufferSize < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.bufferSize);

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
