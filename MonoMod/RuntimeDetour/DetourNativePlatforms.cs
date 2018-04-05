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
    public interface IDetourNativePlatform {
        DetourNative Create(IntPtr from, IntPtr to);
        void Free(DetourNative detour);
        void Copy(DetourNative detour, IntPtr dst);
        void MakeWritable(DetourNative detour);
        void MakeExecutable(DetourNative detour);
    }

    public unsafe sealed class DetourNativeX86Platform : IDetourNativePlatform {
        private const int SIZE64BIT = 1 + 1 + 4 + 8;
        private const int SIZE32BIT = 1 + 4 + 1;

        private static bool Is64BitTarget(IntPtr to)
            => (((ulong) to) & 0x00000000FFFFFFFF) != ((ulong) to);

        private static int Size(IntPtr to) {
            if (Is64BitTarget(to))
                return SIZE64BIT;
            return SIZE32BIT;
        }

        public DetourNative Create(IntPtr from, IntPtr to) {
            DetourNative detour = new DetourNative {
                Method = from,
                Target = to
            };
            detour.Size = Size(to);
            return detour;
        }

        public void Free(DetourNative detour) {
            // No extra data.
        }

        public void Apply(DetourNative detour) {
            int offs = 0;

            if (Is64BitTarget(detour.Target)) {
                // PUSH can only push 32-bit values and MOV RAX, <to>; JMP RAX voids RAX.
                // Registerless JMP [rip+0] + data "absolute jump."

                // JMP [rip+0]
                detour.Method.Write(ref offs, (byte) 0xFF);
                detour.Method.Write(ref offs, (byte) 0x25);
                detour.Method.Write(ref offs, (uint) 0x00000000);

                // <to>
                detour.Method.Write(ref offs, (ulong) detour.Target);

                return;
            }

            // Registerless PUSH + RET "absolute jump."

            // PUSH <to>
            detour.Method.Write(ref offs, (byte) 0x68);
            detour.Method.Write(ref offs, (uint) detour.Target);

            // RET
            detour.Method.Write(ref offs, (byte) 0xC3);
        }

        public void Copy(DetourNative detour, IntPtr dst) {
            if (detour.Size == SIZE64BIT) {
                *((ulong*) ((ulong) dst)) = *((ulong*) ((ulong) detour.Method));
                *((uint*) ((ulong) dst + 8)) = *((uint*) ((ulong) detour.Method + 8));
                *((ushort*) ((ulong) dst + 12)) = *((ushort*) ((ulong) detour.Method + 12));
                return;
            }

            if (detour.Size == SIZE32BIT) {
                *((uint*) ((uint) dst)) = *((uint*) ((uint) detour.Method));
                *((ushort*) ((uint) dst + 4)) = *((ushort*) ((uint) detour.Method + 4));
                return;
            }

            throw new Exception($"Unknown X86 detour size {detour.Size}");
        }

        public void MakeWritable(DetourNative detour) {
            // no-op.
        }

        public void MakeExecutable(DetourNative detour) {
            // no-op.
        }
    }

    public unsafe sealed class DetourNativeARMPlatform : IDetourNativePlatform {
        public void Copy(DetourNative detour, IntPtr dst) {
            throw new NotImplementedException();
        }

        public DetourNative Create(IntPtr from, IntPtr to) {
            throw new NotImplementedException();
        }

        public void Free(DetourNative detour) {
            throw new NotImplementedException();
        }

        public void MakeExecutable(DetourNative detour) {
            throw new NotImplementedException();
        }

        public void MakeWritable(DetourNative detour) {
            throw new NotImplementedException();
        }
    }

    public unsafe sealed class DetourNativeWindowsPlatform : IDetourNativePlatform {
        private IDetourNativePlatform Inner;

        public DetourNativeWindowsPlatform(IDetourNativePlatform inner) {
            Inner = inner;
        }

        [Flags]
        private enum Protection {
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400
        }

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, IntPtr dwSize, Protection flNewProtect, out Protection lpflOldProtect);

        public void MakeWritable(DetourNative detour) {
            Protection oldProtection;
            if (!VirtualProtect(detour.Method, (IntPtr) detour.Size, Protection.PAGE_EXECUTE_READWRITE, out oldProtection))
                throw new System.ComponentModel.Win32Exception();

            Inner.MakeWritable(detour);
        }

        public void MakeExecutable(DetourNative detour) {
            Inner.MakeExecutable(detour);
        }

        public DetourNative Create(IntPtr from, IntPtr to) {
            return Inner.Create(from, to);
        }

        public void Free(DetourNative detour) {
            Inner.Free(detour);
        }

        public void Copy(DetourNative detour, IntPtr dst) {
            Inner.Copy(detour, dst);
        }
    }

}
