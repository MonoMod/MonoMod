using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;

namespace MonoMod.RuntimeDetour {
    public static unsafe class DetourHelper {

        public static void Write(this IntPtr to, ref int offs, byte value) {
            *((byte*) ((long) to + offs)) = value;
            offs += 1;
        }
        public static void Write(this IntPtr to, ref int offs, ushort value) {
            *((ushort*) ((long) to + offs)) = value;
            offs += 2;
        }
        public static void Write(this IntPtr to, ref int offs, uint value) {
            *((uint*) ((long) to + offs)) = value;
            offs += 4;
        }
        public static void Write(this IntPtr to, ref int offs, ulong value) {
            *((ulong*) ((long) to + offs)) = value;
            offs += 8;
        }

    }
}
