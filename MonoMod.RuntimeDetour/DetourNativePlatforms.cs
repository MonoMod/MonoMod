using MonoMod.Utils;
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
        private enum DetourSize : int {
            Rel32 = 1 + 4,
            Abs32 = 1 + 4 + 1,
            Abs64 = 1 + 1 + 4 + 8
        }

        private static bool Is32Bit(long to)
            => (((ulong) to) & 0x00000000FFFFFFFF) == ((ulong) to);

        private static DetourSize GetDetourSize(IntPtr from, IntPtr to) {
            long rel = (long) to - ((long) from + 5);
            /* Note: Check -rel as well, as f.e. FFFFFFFFF58545C0 -> FFFFFFFFF5827030 ends up with rel = FFFFFFFFFFFD2A6B
             * This is critical for some 32-bit environments, as in that case, an Abs64 detour gets emitted on x86 instead!
             * Checking for -rel ensures that backwards jumps are handled properly as well, using Rel32 detours.
             */
            if (Is32Bit(rel) || Is32Bit(-rel))
                return DetourSize.Rel32;

            if (Is32Bit((long) to))
                return DetourSize.Abs32;

            return DetourSize.Abs64;
        }

        public NativeDetourData Create(IntPtr from, IntPtr to) {
            NativeDetourData detour = new NativeDetourData {
                Method = from,
                Target = to
            };
            detour.Size = (int) GetDetourSize(from, to);
            return detour;
        }

        public void Free(NativeDetourData detour) {
            // No extra data.
        }

        public void Apply(NativeDetourData detour) {
            int offs = 0;

            // Console.WriteLine($"Detour {((ulong) detour.Method):X16} -> {((ulong) detour.Target):X16}, {((DetourSize) detour.Size)}");
            switch ((DetourSize) detour.Size) {
                case DetourSize.Rel32:
                    // JMP DeltaNextInstr
                    detour.Method.Write(ref offs, (byte) 0xE9);
                    detour.Method.Write(ref offs, (uint) (int) (
                        (long) detour.Target - ((long) detour.Method + offs + sizeof(uint))
                    ));
                    break;

                case DetourSize.Abs32:
                    // Registerless PUSH + RET "absolute jump."
                    // PUSH <to>
                    detour.Method.Write(ref offs, (byte) 0x68);
                    detour.Method.Write(ref offs, (uint) detour.Target);
                    // RET
                    detour.Method.Write(ref offs, (byte) 0xC3);
                    break;

                case DetourSize.Abs64:
                    // PUSH can only push 32-bit values and MOV RAX, <to>; JMP RAX voids RAX.
                    // Registerless JMP [rip+0] + data "absolute jump."
                    // JMP [rip+0]
                    detour.Method.Write(ref offs, (byte) 0xFF);
                    detour.Method.Write(ref offs, (byte) 0x25);
                    detour.Method.Write(ref offs, (uint) 0x00000000);
                    // <to>
                    detour.Method.Write(ref offs, (ulong) detour.Target);
                    break;

                default:
                    throw new NotSupportedException($"Unknown X86 detour size {detour.Size}");
            }
        }

        public void Copy(IntPtr src, IntPtr dst, int size) {
            switch ((DetourSize) size) {
                case DetourSize.Rel32:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(byte*) ((ulong) dst + 4) = *(byte*) ((ulong) src + 4);
                    break;

                case DetourSize.Abs32:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(ushort*) ((ulong) dst + 4) = *(ushort*) ((ulong) src + 4);
                    break;

                case DetourSize.Abs64:
                    *(ulong*) ((ulong) dst) = *(ulong*) ((ulong) src);
                    *(uint*) ((ulong) dst + 8) = *(uint*) ((ulong) src + 8);
                    *(ushort*) ((ulong) dst + 12) = *(ushort*) ((ulong) src + 12);
                    break;

                default:
                    throw new NotSupportedException($"Unknown X86 detour size {size}");
            }
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
        private enum DetourSize : int {
            Arm32 = 4 + 4,
            Arm64 = 4 + 4 + 8
        }

        public NativeDetourData Create(IntPtr from, IntPtr to) {
            NativeDetourData detour = new NativeDetourData {
                Method = from,
                Target = to
            };
            detour.Size = (int) (IntPtr.Size == 4 ? DetourSize.Arm32 : DetourSize.Arm64);
            return detour;
        }

        public void Free(NativeDetourData detour) {
            // No extra data.
        }

        public void Apply(NativeDetourData detour) {
            int offs = 0;
            
            switch ((DetourSize) detour.Size) {
                case DetourSize.Arm32:
                    // FIXME: This fails on arm64.
                    // LDR PC, [PC, #-4]
                    detour.Method.Write(ref offs, (byte) 0x04);
                    detour.Method.Write(ref offs, (byte) 0xF0);
                    detour.Method.Write(ref offs, (byte) 0x1F);
                    detour.Method.Write(ref offs, (byte) 0xE5);
                    // <to>
                    detour.Method.Write(ref offs, (uint) detour.Target);
                    break;

                case DetourSize.Arm64:
                    // FIXME: This fails on mono.
                    // PC isn't available on arm64.
                    // We need to burn a register and branch instead.
                    // Please check / update https://github.com/0x0ade/MonoMod/issues/38
                    // LDR X15, #8
                    detour.Method.Write(ref offs, (byte) 0x4F);
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0x58);
                    // BR X15
                    detour.Method.Write(ref offs, (byte) 0xE0);
                    detour.Method.Write(ref offs, (byte) 0x01);
                    detour.Method.Write(ref offs, (byte) 0x1F);
                    detour.Method.Write(ref offs, (byte) 0xD6);
                    // <to>
                    detour.Method.Write(ref offs, (ulong) detour.Target);
                    break;

                default:
                    throw new NotSupportedException($"Unknown ARM detour size {detour.Size}");
            }
        }

        public void Copy(IntPtr src, IntPtr dst, int size) {
            switch ((DetourSize) size) {
                case DetourSize.Arm32:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(uint*) ((ulong) dst + 4) = *(uint*) ((ulong) src + 4);
                    break;

                case DetourSize.Arm64:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(uint*) ((ulong) dst + 4) = *(uint*) ((ulong) src + 4);
                    *(ulong*) ((ulong) dst + 8) = *(ulong*) ((ulong) src + 8);
                    break;

                default:
                    throw new NotSupportedException($"Unknown ARM detour size {size}");
            }
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
