using System;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour {
    public interface IDetourNativePlatform {
        NativeDetourData Create(IntPtr from, IntPtr to);
        void Free(NativeDetourData detour);
        void Apply(NativeDetourData detour);
        void Copy(IntPtr src, IntPtr dst, int size);
        void MakeWritable(NativeDetourData detour);
        void MakeExecutable(NativeDetourData detour);
        IntPtr MemAlloc(int size);
        void MemFree(IntPtr ptr);
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

        public NativeDetourData Create(IntPtr from, IntPtr to) {
            NativeDetourData detour = new NativeDetourData {
                Method = from,
                Target = to
            };
            detour.Size = Size(to);
            return detour;
        }

        public void Free(NativeDetourData detour) {
            // No extra data.
        }

        public void Apply(NativeDetourData detour) {
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

        public void Copy(IntPtr src, IntPtr dst, int size) {
            if (size == SIZE64BIT) {
                *((ulong*) ((ulong) dst)) = *((ulong*) ((ulong) src));
                *((uint*) ((ulong) dst + 8)) = *((uint*) ((ulong) src + 8));
                *((ushort*) ((ulong) dst + 12)) = *((ushort*) ((ulong) src + 12));
                return;
            }

            if (size == SIZE32BIT) {
                *((uint*) ((uint) dst)) = *((uint*) ((uint) src));
                *((ushort*) ((uint) dst + 4)) = *((ushort*) ((uint) src + 4));
                return;
            }

            throw new Exception($"Unknown X86 detour size {size}");
        }

        public void MakeWritable(NativeDetourData detour) {
            // no-op.
        }

        public void MakeExecutable(NativeDetourData detour) {
            // no-op.
        }

        public IntPtr MemAlloc(int size) {
            return Marshal.AllocHGlobal(size);
        }

        public void MemFree(IntPtr ptr) {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public unsafe sealed class DetourNativeARMPlatform : IDetourNativePlatform {
        public void Apply(NativeDetourData detour) {
            throw new NotImplementedException();
        }

        public void Copy(IntPtr src, IntPtr dst, int size) {
            throw new NotImplementedException();
        }

        public NativeDetourData Create(IntPtr from, IntPtr to) {
            throw new NotImplementedException();
        }

        public void Free(NativeDetourData detour) {
            throw new NotImplementedException();
        }

        public void MakeExecutable(NativeDetourData detour) {
            throw new NotImplementedException();
        }

        public void MakeWritable(NativeDetourData detour) {
            throw new NotImplementedException();
        }

        public IntPtr MemAlloc(int size) {
            throw new NotImplementedException();
        }

        public void MemFree(IntPtr ptr) {
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

        public void MakeWritable(NativeDetourData detour) {
            Protection oldProtection;
            if (!VirtualProtect(detour.Method, (IntPtr) detour.Size, Protection.PAGE_EXECUTE_READWRITE, out oldProtection))
                throw new System.ComponentModel.Win32Exception();

            Inner.MakeWritable(detour);
        }

        public void MakeExecutable(NativeDetourData detour) {
            Protection oldProtection;
            if (!VirtualProtect(detour.Method, (IntPtr) detour.Size, Protection.PAGE_EXECUTE_READWRITE, out oldProtection))
                throw new System.ComponentModel.Win32Exception();

            Inner.MakeExecutable(detour);
        }

        public NativeDetourData Create(IntPtr from, IntPtr to) {
            return Inner.Create(from, to);
        }

        public void Free(NativeDetourData detour) {
            Inner.Free(detour);
        }

        public void Apply(NativeDetourData detour) {
            Inner.Apply(detour);
        }

        public void Copy(IntPtr src, IntPtr dst, int size) {
            Inner.Copy(src, dst, size);
        }

        public IntPtr MemAlloc(int size) {
            return Inner.MemAlloc(size);
        }

        public void MemFree(IntPtr ptr) {
            Inner.MemFree(ptr);
        }
    }

}
