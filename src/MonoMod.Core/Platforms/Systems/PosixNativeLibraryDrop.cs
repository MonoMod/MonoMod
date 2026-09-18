using MonoMod.Utils;
using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace MonoMod.Core.Platforms.Systems;

internal abstract class PosixNativeLibraryDrop
{
    /// <summary>
    /// Follows POSIX <c>mkstemp(3)</c> rules.
    /// </summary>
    /// <param name="template">The UTF-8 template buffer to fill. This buffer is guaranteed to have at least 1 trailing null byte.</param>
    /// <returns>A file descriptor.</returns>
    protected abstract nint Mkstemp(Span<byte> template);

    /// <summary>
    /// Follows POSIX <c>close(2)</c> rules. Closes the provided OS file descriptor.
    /// </summary>
    /// <param name="fd">The file descriptor to close. Recieved from <see cref="Mkstemp(Span{byte})"/>.</param>
    protected abstract void CloseFileDescriptor(nint fd);

    /// <summary>
    /// Drops the library contained in <paramref name="sourceStream"/> to disk at a unique path to be loaded.
    /// </summary>
    /// <param name="sourceStream">The source data to drop.</param>
    /// <param name="defaultTemplate">The <c>mkstemp(3)</c> template to use by default. If the user overrides the drop path,
    /// everything after the final <c>/</c> will be appended to the user-provided drop path.</param>
    /// <returns>The full final path of the dropped binary.</returns>
    public unsafe string DropLibrary(Stream sourceStream, ReadOnlySpan<byte> defaultTemplate)
    {
        int templateLength;
        byte[] templ;
        if (Switches.TryGetSwitchValue(Switches.HelperDropPath, out var value) && value is string dropPath)
        {
            var endOfDefaultTemplateDir = defaultTemplate.LastIndexOf((byte)'/');
            Helpers.Assert(endOfDefaultTemplateDir >= 0);
            var templateBasename = defaultTemplate.Slice(endOfDefaultTemplateDir);

            dropPath = Path.GetFullPath(dropPath);
            _ = Directory.CreateDirectory(dropPath);

            var byteCount = Encoding.UTF8.GetByteCount(dropPath);
            templ = ArrayPool<byte>.Shared.Rent(byteCount + templateBasename.Length + 1);
            templ.AsSpan().Clear();
            int pos;
            // encode the source path into the start of the buffer
            fixed (char* pchars = dropPath.AsSpan())
            fixed (byte* pbytes = templ)
            {
                pos = Encoding.UTF8.GetBytes(pchars, dropPath.Length, pbytes, templ.Length);
            }

            if (templ[pos - 1] is (byte)'/')
            {
                // we already have the leading slash on the template basename, so overwrite the one here
                pos--;
            }

            // copy the basename into the end
            templateBasename.CopyTo(templ.AsSpan(pos));
            // and just for good measure, null terminate
            templ[pos + templateBasename.Length] = 0;
            templateLength = pos + templateBasename.Length;
        }
        else
        {
            templ = ArrayPool<byte>.Shared.Rent(defaultTemplate.Length + 1);
            templ.AsSpan().Clear();
            defaultTemplate.CopyTo(templ);
            templateLength = defaultTemplate.Length;
        }

        var fd = Mkstemp(templ);
        var fname = Encoding.UTF8.GetString(templ, 0, templateLength);

        ArrayPool<byte>.Shared.Return(templ);

        if (PlatformDetection.Runtime is RuntimeKind.Mono && PlatformDetection.Corelib is not CorelibKind.Core)
        {
            // Mono (specifically .NET Framework Mono, not .NET Core Mono) doesn't accept an OS FD to FileStream on Unix.
            // Instead, we close the handle and reopen the path, and basically just hope that the TOCTOU doesn't matter.
            CloseFileDescriptor(fd);

            using var fs = new FileStream(fname, FileMode.Create, FileAccess.Write);
            sourceStream.CopyTo(fs);
        }
        else
        {
            // Everywhere else uses actual OS handles, so we can do the "correct" thing which avoids (more of) the TOCTOU
            // windows. Even this leaves open a small window before we load the assembly, but there's not a lot we can do
            // about that.
            try
            {
                // SafeFileHandle overload of FileStream constructor is recommended, but it's missing on some old Unity mono builds.
#pragma warning disable CS0618 // Type or member is obsolete
                using var fs = new FileStream(fd, FileAccess.Write);
#pragma warning restore CS0618 // Type or member is obsolete
                sourceStream.CopyTo(fs);
            }
            finally
            {
                CloseFileDescriptor(fd);
            }
        }

        return fname;
    }

}
