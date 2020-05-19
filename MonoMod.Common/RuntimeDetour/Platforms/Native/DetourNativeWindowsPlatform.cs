using MonoMod.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    unsafe class DetourNativeWindowsPlatform : IDetourNativePlatform {
        private readonly IDetourNativePlatform Inner;

        public DetourNativeWindowsPlatform(IDetourNativePlatform inner) {
            Inner = inner;
        }

        public void MakeWritable(IntPtr src, uint size) {
            // PAGE_READWRITE causes an AccessViolationException / TargetInvocationException.
            if (!VirtualProtect(src, (IntPtr) size, Protection.PAGE_EXECUTE_READWRITE, out _)) {
                LogAllSections("MakeWriteable", src, size);
                throw new Win32Exception();
            }
        }

        public void MakeExecutable(IntPtr src, uint size) {
            if (!VirtualProtect(src, (IntPtr) size, Protection.PAGE_EXECUTE_READWRITE, out _)) {
                LogAllSections("MakeExecutable", src, size);
                throw new Win32Exception();
            }
        }

        public void FlushICache(IntPtr src, uint size) {
            if (!FlushInstructionCache(GetCurrentProcess(), src, (UIntPtr) size)) {
                LogAllSections("FlushICache", src, size);
                throw new Win32Exception();
            }
        }

        private void LogAllSections(string from, IntPtr src, uint size) {
            MMDbgLog.Log($"{from} failed for {(long) src:X16} + {size} - logging all memory sections");

            IntPtr proc = Process.GetCurrentProcess().Handle;
            IntPtr addr = (IntPtr) 0x00000000000010000;
            int i = 0;
            while (true) {
                if (VirtualQueryEx(proc, addr, out MemInfo info, sizeof(MemInfo)) == 0)
                    break;

                long regionSize = (long) info.RegionSize;
                if (regionSize <= 0 || (int) regionSize != regionSize) {
                    if (IntPtr.Size == 8) {
                        addr = (IntPtr) ((ulong) info.BaseAddress + (ulong) info.RegionSize);
                        continue;
                    }
                    break;
                }

                MMDbgLog.Log($"#{i++}: addr: 0x{(long) info.BaseAddress:X16}; protect: 0x{info.Protect:X8}; state: 0x{info.State:X8}; type: 0x{info.Type:X8}; size: 0x{(long) info.RegionSize:X16}");

                addr = (IntPtr) ((long) info.BaseAddress + regionSize);
            }
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

        public IntPtr MemAlloc(uint size) {
            return Inner.MemAlloc(size);
        }

        public void MemFree(IntPtr ptr) {
            Inner.MemFree(ptr);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, IntPtr dwSize, Protection flNewProtect, out Protection lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MemInfo lpBuffer, int dwLength);

        [Flags]
        private enum Protection : uint {
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

        [StructLayout(LayoutKind.Sequential)]
        public struct MemInfo {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }
    }
}
