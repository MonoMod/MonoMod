using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop {
    internal static class Unix {
        // If this dllimport decl isn't enough to get the runtime to load the right thing, I give up
        public const string LibC = "libc";

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        public static extern unsafe int Uname(byte* buf);
    }
}
