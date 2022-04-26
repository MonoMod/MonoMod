using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop {
    internal static class OSX {

        public const string LibSystem = "libSystem";

        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        public static extern unsafe int Uname(byte* buf);
    }
}
