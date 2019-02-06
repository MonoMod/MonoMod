using MonoMod.Utils;
using System;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour.Platforms {
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

        public void MakeWritable(NativeDetourData detour) {
            Protection oldProtection;
            if (!VirtualProtect(detour.Method, (IntPtr) detour.Size, Protection.PAGE_READWRITE, out oldProtection))
                throw new System.ComponentModel.Win32Exception();

            Inner.MakeWritable(detour);
        }

        public void MakeExecutable(NativeDetourData detour) {
            Protection oldProtection;
            if (!VirtualProtect(detour.Method, (IntPtr) detour.Size, Protection.PAGE_EXECUTE_READ, out oldProtection))
                throw new System.ComponentModel.Win32Exception();

            Inner.MakeExecutable(detour);
        }

        public NativeDetourData Create(IntPtr from, IntPtr to, byte? type) {
            return Inner.Create(from, to, type);
        }

        public void Free(NativeDetourData detour) {
            Inner.Free(detour);
        }

        public void Apply(NativeDetourData detour) {
            Inner.Apply(detour);
        }

        public void Copy(IntPtr src, IntPtr dst, byte type) {
            Inner.Copy(src, dst, type);
        }

        public IntPtr MemAlloc(int size) {
            return Inner.MemAlloc(size);
        }

        public void MemFree(IntPtr ptr) {
            Inner.MemFree(ptr);
        }
    }

}
