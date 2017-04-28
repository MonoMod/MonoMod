using MonoMod.InlineRT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MonoMod.Detour {
    public static class RuntimeDetour {

        public static bool IsX64 { get; } = (PlatformHelper.Current & Platform.X64) == Platform.X64;

        public static unsafe void* GetMethodStart(MethodBase method) {
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            return method.MethodHandle.GetFunctionPointer().ToPointer();
        }
        public static unsafe void* GetDelegateStart(Delegate d) {
            RuntimeHelpers.PrepareDelegate(d);
            return Marshal.GetFunctionPointerForDelegate(d).ToPointer();
        }

        public static unsafe void Detour(this MethodBase from, IntPtr to)
            => Detour(GetMethodStart(from), to.ToPointer());

        public static unsafe void Detour(this MethodBase from, MethodBase to)
            => Detour(GetMethodStart(from), GetMethodStart(to));

        public static unsafe void Detour(this MethodBase from, Delegate to)
            => Detour(GetMethodStart(from), GetDelegateStart(to));

        public static unsafe void DetourMethod(IntPtr from, IntPtr to)
            => Detour(from.ToPointer(), to.ToPointer());

        public static unsafe void Detour(void* from, void* to) {
            if (IsX64) {
                *((byte*)  ((ulong) from))      = 0x48;
                *((byte*)  ((ulong) from + 1))  = 0xB8;
                *((ulong*) ((ulong) from + 2))  = (ulong) to;
                *((byte*)  ((ulong) from + 10)) = 0xFF;
                *((byte*)  ((ulong) from + 11)) = 0xE0;
            } else {
                *((byte*)  ((ulong) from))      = 0x68;
                *((uint*)  ((ulong) from + 1))  = (uint) to;
                *((byte*)  ((ulong) from + 5))  = 0xC3;
            }
        }

    }
}
