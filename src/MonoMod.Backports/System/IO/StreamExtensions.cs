using System.Buffers;

namespace System.IO {
    public static class StreamExtensions {
        public static void CopyTo(this Stream src, Stream dest) => CopyTo(src, dest, 0x4000);
        public static void CopyTo(this Stream src, Stream dest, int bufSize) {
            if (src is null)
                throw new ArgumentNullException(nameof(src));
            if (dest is null)
                throw new ArgumentNullException(nameof(dest));
            if (bufSize < 0)
                throw new ArgumentOutOfRangeException(nameof(bufSize));

            var buf = ArrayPool<byte>.Shared.Rent(bufSize);

            try {
                int read;
                do {
                    read = src.Read(buf, 0, buf.Length);
                    if (read > 0) {
                        dest.Write(buf, 0, read);
                    }
                } while (read > 0);
            } finally {
                ArrayPool<byte>.Shared.Return(buf, true);
            }
        }
    }
}
