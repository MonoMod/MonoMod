using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.Core.Interop {
    internal static class OSX {

        public const string LibSystem = "libSystem";

        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        public static extern unsafe int Uname(byte* buf);
    }
}
