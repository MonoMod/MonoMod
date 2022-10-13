using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MonoMod.Utils.Interop {
    [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes",
        Justification = "The attribute doesn't do anything on platforms where this will be used.")]
    internal static class OSX {
        public const string LibSystem = "libSystem";

        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        public static extern unsafe int Uname(byte* buf);
    }
}
