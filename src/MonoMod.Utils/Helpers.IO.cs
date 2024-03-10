using System;
using System.Buffers;
using System.IO;

namespace MonoMod.Utils
{
    public partial class Helpers
    {
        // Oh Unity, how we hate thee.
        // The Unity implementation of File.ReadAllBytes does not correctly handle 0 but-not-actually-0 length files. The implementation below
        // is copied from CoreFX, and is very close to the modern CoreCLR APis, with the exception that they open a SafeHandle and use RandomAccess
        // when the length is actually known. We obviously can't do that, so we use the older implementation which reads out of the stream.

        public static byte[] ReadAllBytes(string path)
        {
            // bufferSize == 1 used to avoid unnecessary buffer in FileStream
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1))
            {
                var fileLength = fs.Length;
                if (fileLength > int.MaxValue)
                {
                    throw new IOException("File is too long (more than 2GB)");
                }
                else if (fileLength == 0)
                {
                    // Some file systems (e.g. procfs on Linux) return 0 for length even when there's content.
                    // Thus we need to assume 0 doesn't mean empty.
                    return ReadAllBytesUnknownLength(fs);
                }

                var index = 0;
                var count = (int)fileLength;
                var bytes = new byte[count];
                while (count > 0)
                {
                    var n = fs.Read(bytes, index, count);
                    if (n == 0)
                        throw new IOException("Unexpected end of stream");
                    index += n;
                    count -= n;
                }
                return bytes;
            }
        }

        private static byte[] ReadAllBytesUnknownLength(FileStream fs)
        {
            var rentedArray = ArrayPool<byte>.Shared.Rent(256);
            try
            {
                var bytesRead = 0;
                while (true)
                {
                    if (bytesRead == rentedArray.Length)
                    {
                        var newLength = (uint)rentedArray.Length * 2;
                        if (newLength > ArrayEx.MaxLength)
                        {
                            newLength = (uint)Math.Max(ArrayEx.MaxLength, rentedArray.Length + 1);
                        }

                        var tmp = ArrayPool<byte>.Shared.Rent((int)newLength);
                        Array.Copy(rentedArray, tmp, rentedArray.Length);
                        if (rentedArray != null)
                        {
                            ArrayPool<byte>.Shared.Return(rentedArray);
                        }
                        rentedArray = tmp;
                    }

                    DAssert(bytesRead < rentedArray.Length);
                    var n = fs.Read(rentedArray, bytesRead, rentedArray.Length - bytesRead);
                    if (n == 0)
                    {
                        return rentedArray.AsSpan(0, bytesRead).ToArray();
                    }
                    bytesRead += n;
                }
            }
            finally
            {
                if (rentedArray != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedArray);
                }
            }
        }

    }
}
