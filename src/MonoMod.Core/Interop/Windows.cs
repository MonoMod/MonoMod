using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop {

    // IMPORTANT: Instead of manually writing the interop code for Windows, we mostly use Microsoft.Windows.CsWin32 to generate them.
    // New Win32 methods should be added to NativeMethods.txt and used as Windows.Win32.Interop.*

    internal static class Windows {
        public const string Kernel32 = "Kernel32";


        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION {
            public nuint BaseAddress;
            public nuint AllocationBase;
            public global::Windows.Win32.System.Memory.PAGE_PROTECTION_FLAGS AllocationProtect;
            public nuint RegionSize;
            public global::Windows.Win32.System.Memory.VIRTUAL_ALLOCATION_TYPE State;
            public global::Windows.Win32.System.Memory.PAGE_PROTECTION_FLAGS Protect;
            public global::Windows.Win32.System.Memory.PAGE_TYPE Type;
        }

        // We do need to manually write VirtualQuery because CsWin32 refuses to generate it becuase it's supposedly arch-specific.

        [DllImport(Kernel32, SetLastError = true)]
        public unsafe static extern int VirtualQuery(void* lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, nuint dwLength);
    }

}
