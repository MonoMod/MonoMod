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
    public static class RuntimeDetourHelper {

        public static bool IsX64 { get; } = (PlatformHelper.Current & Platform.X64) == Platform.X64;
        public static bool IsDebug => Debugger.IsAttached;

        internal static RuntimeDetourHelperImpl _HelperImpl;

        public static unsafe IntPtr GetMethodStart(this MethodBase method)
            => new IntPtr(_HelperImpl.GetMethodStart(method));

        public static unsafe void DetourJIT(IntPtr from, IntPtr to)
            => _HelperImpl.Detour(from.ToPointer(), to.ToPointer());

        public static unsafe void DetourJIT(this MethodBase from, IntPtr to)
            => _HelperImpl.Detour(_HelperImpl.GetMethodStart(from), to.ToPointer());

        public static unsafe void DetourJIT(this MethodBase from, MethodBase to)
            => _HelperImpl.Detour(_HelperImpl.GetMethodStart(from), _HelperImpl.GetMethodStart(to));

        public static unsafe void DetourJIT(this MethodBase from, Delegate to)
            => _HelperImpl.Detour(_HelperImpl.GetMethodStart(from), _HelperImpl.GetDelegateStart(to));


        public static unsafe void SetNInt(void* p, ulong v) {
            if (IsX64)
                (*(ulong*) p) = v;
            else
                (*(uint*) p) = (uint) v;
        }
        public static unsafe ulong GetNInt(void* p) {
            if (IsX64)
                return *(ulong*) p;
            else
                return *(uint*) p;
        }


        static RuntimeDetourHelper() {
            // Helps tracking possible future version differences
            if (Type.GetType("Mono.Runtime") != null)
                _HelperImpl = new MonoDetourHelperImpl();
            else
                _HelperImpl = new NetDetourHelperImpl();
        }

        internal abstract class RuntimeDetourHelperImpl {

            public abstract unsafe void* GetMethodStart(MethodBase method);
            public abstract unsafe void* GetDelegateStart(Delegate d);

            public abstract unsafe void Detour(void* from, void* to);

        }

        // FIXME
        internal class NetDetourHelperImpl : RuntimeDetourHelperImpl {

            public override unsafe void* GetMethodStart(MethodBase method) {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);

                ulong addr = unchecked((ulong) method.MethodHandle.Value.ToInt64());
                if (IsX64)
                    addr += 1;
                else
                    addr += 2;

                if (method.IsVirtual) {
                    byte index = *(byte*) (addr + 7);
                    ulong offs = unchecked((ulong) method.DeclaringType.TypeHandle.Value.ToInt64());
                    if (IsX64) {
                        offs = *(ulong*) (offs + 8);
                    } else {
                        offs = *(uint*) (offs + 10);
                    }
                    addr = offs + index;
                }

                return (void*) addr;
            }

            public override unsafe void* GetDelegateStart(Delegate d)
                => Marshal.GetFunctionPointerForDelegate(d).ToPointer();

            public override unsafe void Detour(void* from, void* to) {
                if (!IsDebug) {
                    Console.WriteLine($".NET Release detour: {(ulong) from} {(ulong) to}");
                    SetNInt(from, GetNInt(to));
                } else {
                    Console.WriteLine($".NET Debug detour: {(ulong) from} {(ulong) to}");
                    byte* from_ = (byte*) GetNInt(from);
                    byte* to_ = (byte*) GetNInt(to);
                    *((int*) ((ulong) to + 1)) = (((int) from + 5) + *((int*) ((ulong) from + 1))) - ((int) to + 5);
                }
            }

        }

        internal class MonoDetourHelperImpl : RuntimeDetourHelperImpl {

            public override unsafe void* GetMethodStart(MethodBase method)
                => method.MethodHandle.GetFunctionPointer().ToPointer();
            public override unsafe void* GetDelegateStart(Delegate d)
                => Marshal.GetFunctionPointerForDelegate(d).ToPointer();

            public override unsafe void Detour(void* from, void* to) {
                if (IsX64) {
                    *((byte*)  ((ulong) from))      = 0x48;
                    *((byte*)  ((ulong) from + 1))  = 0xB8;
                    *((ulong*) ((ulong) from + 2))  = (ulong) to;
                    *((byte*)  ((ulong) from + 10)) = 0xFF;
                    *((byte*)  ((ulong) from + 11)) = 0xE0;
                } else {
                    *((byte*) ((ulong) from))       = 0x68;
                    *((uint*) ((ulong) from + 1))   = (uint) to;
                    *((byte*) ((ulong) from + 5))   = 0xC3;
                }
            }

        }

    }
}
