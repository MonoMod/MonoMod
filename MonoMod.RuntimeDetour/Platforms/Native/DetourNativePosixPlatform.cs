using MonoMod.Utils;
using System;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour.Platforms {
    public unsafe sealed class DetourNativePosixPlatform : IDetourNativePlatform {
        private IDetourNativePlatform Inner;

        public DetourNativePosixPlatform(IDetourNativePlatform inner) {
            Inner = inner;
        }

        [Flags]
        private enum MmapProts : int {
            PROT_READ = 0x1,
            PROT_WRITE = 0x2,
            PROT_EXEC = 0x4,
            PROT_NONE = 0x0,
            PROT_GROWSDOWN = 0x01000000,
            PROT_GROWSUP = 0x02000000,
        }

        // Good luck if your copy of Mono doesn't ship with MonoPosixHelper...
        [DllImport("MonoPosixHelper", SetLastError = true, EntryPoint = "Mono_Posix_Syscall_mprotect")]
        private static extern int mprotect(IntPtr start, ulong len, MmapProts prot);

        public void MakeWritable(NativeDetourData detour) {
            // RWX because old versions of mono always use RWX.
            mprotect(detour.Method, detour.Size, MmapProts.PROT_READ | MmapProts.PROT_WRITE | MmapProts.PROT_EXEC);
            Inner.MakeWritable(detour);
        }

        public void MakeExecutable(NativeDetourData detour) {
            // RWX because old versions of mono always use RWX.
            mprotect(detour.Method, detour.Size, MmapProts.PROT_READ | MmapProts.PROT_WRITE | MmapProts.PROT_EXEC);
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

        public IntPtr MemAlloc(uint size) {
            return Inner.MemAlloc(size);
        }

        public void MemFree(IntPtr ptr) {
            Inner.MemFree(ptr);
        }
    }
}
