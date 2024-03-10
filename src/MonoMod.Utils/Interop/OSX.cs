using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MonoMod.Utils.Interop
{
    [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes",
        Justification = "The attribute doesn't do anything on platforms where this will be used.")]
    internal static partial class OSX
    {
        public const string LibSystem = "libSystem";

        // We have to do these shenanigans, because we *need* SetLastError; this can set errno.
        // SetLastError on DllImport involves an ILStub, and DisableRuntimeMarshalling prevents that.
        // LibraryImport can't be used downlevel for this, because it relies on Marshal.GetLastSystemError(), which is new in .NET 6.
#if NET7_0_OR_GREATER
        [LibraryImport(LibSystem, EntryPoint = "uname", SetLastError = true)]
        public static unsafe partial int Uname(byte* buf);
#else
        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        public static extern unsafe int Uname(byte* buf);
#endif
    }
}
