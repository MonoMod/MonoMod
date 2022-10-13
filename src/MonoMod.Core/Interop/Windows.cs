using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop {
    internal static class Windows {

        public const string Kernel32 = "Kernel32";

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct SystemInfo {
            public ushort wProcessorArchitecture;
            public ushort wReserved1;
            public uint dwPageSize;
            public void* lpMinAppAddr;
            public void* lpMaxAppAddr;
            public nint dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }

        [Flags]
        public enum PAGE : uint {
            UNSET,
            NOACCESS =
                0b00000000000000000000000000000001,
            READONLY =
                0b00000000000000000000000000000010,
            READWRITE =
                0b00000000000000000000000000000100,
            WRITECOPY =
                0b00000000000000000000000000001000,
            EXECUTE =
                0b00000000000000000000000000010000,
            EXECUTE_READ =
                0b00000000000000000000000000100000,
            EXECUTE_READWRITE =
                0b00000000000000000000000001000000,
            EXECUTE_WRITECOPY =
                0b00000000000000000000000010000000,
            GUARD =
                0b00000000000000000000000100000000,
            NOCACHE =
                0b00000000000000000000001000000000,
            WRITECOMBINE =
                0b00000000000000000000010000000000,
        }

        public enum MEM : uint {
            UNSET,
            DECOMMIT = 0x00004000,
            RELEASE = 0x00008000,
            COMMIT =
                0b00000000000000000001000000000000,
            RESERVE =
                0b00000000000000000010000000000000,
            FREE =
                0b00000000000000010000000000000000,
            PRIVATE =
                0b00000000000000100000000000000000,
            MAPPED =
                0b00000000000001000000000000000000,
            IMAGE =
                0b00000001000000000000000000000000,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public PAGE AllocationProtect;
            public IntPtr RegionSize;
            public MEM State;
            public PAGE Protect;
            public MEM Type;
        }

        [DllImport(Kernel32, SetLastError = false)]
        public static extern void GetSystemInfo(out SystemInfo lpSystemInfo);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr lpAddress, nint dwSize, MEM flAllocationType, PAGE flProtect);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern bool VirtualProtect(IntPtr lpAddress, nint dwSize, PAGE flNewProtect, out PAGE lpflOldProtect);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern bool VirtualFree(IntPtr lpAddress, nint dwSize, MEM dwFreeType);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport(Kernel32, SetLastError = true)]
        public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nint dwSize);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern int VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);
    }
}
