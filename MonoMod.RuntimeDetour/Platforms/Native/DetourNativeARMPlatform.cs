using MonoMod.Utils;
using System;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour.Platforms {
    public unsafe sealed class DetourNativeARMPlatform : IDetourNativePlatform {
        // TODO: Make use of possibly shorter near branches.
        public enum DetourType : byte {
            Thumb,
            ThumbX,
            AArch32,
            AArch32X,
            AArch64
        }
        private static readonly uint[] DetourSizes = {
            2 + 2 + 4,
            2 + 2 + 4,
            4 + 4,
            4 + 4 + 4,
            4 + 4 + 8
        };

        private static DetourType GetDetourType(IntPtr from, IntPtr to) {
            // The lowest bit is set for Thumb, unset for ARM.
            if (((ulong) from & 0x1) == 0x1)
                return DetourType.ThumbX;

            if (IntPtr.Size == 4)
                return DetourType.AArch32X;

            return DetourType.AArch64;
        }

        public NativeDetourData Create(IntPtr from, IntPtr to, byte? type) {
            NativeDetourData detour = new NativeDetourData {
                Method = from,
                Target = to
            };
            detour.Size = DetourSizes[detour.Type = type ?? (byte) GetDetourType(from, to)];
            // Console.WriteLine($"{nameof(DetourNativeARMPlatform)} create: {(DetourType) detour.Type} 0x{((ulong) detour.Method).ToString("X16")} + 0x{detour.Size.ToString("X8")} -> 0x{((ulong) detour.Target).ToString("X16")}");
            return detour;
        }

        public void Free(NativeDetourData detour) {
            // No extra data.
        }

        public void Apply(NativeDetourData detour) {
            int offs = 0;

            // Console.WriteLine($"{nameof(DetourNativeARMPlatform)} apply: {(DetourType) detour.Type} 0x{((ulong) detour.Method).ToString("X16")} -> 0x{((ulong) detour.Target).ToString("X16")}");
            switch ((DetourType) detour.Type) {
                case DetourType.Thumb:
                    // TODO: Short range Thumb branch using B
                case DetourType.ThumbX:
                    // Burn a register to stay safe.
                    // Note: PC is 4 bytes ahead
                    // R12 is available but out of Thumb scope.
                    // LDR R7, [PC, #0]
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0x4F);
                    // BX R7
                    detour.Method.Write(ref offs, (byte) 0x38);
                    detour.Method.Write(ref offs, (byte) 0x47);
                    // <to>
                    detour.Method.Write(ref offs, (uint) detour.Target);
                    break;

                case DetourType.AArch32:
                    // FIXME: This fails on dotnet for arm running on aarch64.
                    // Note: PC is 8 bytes ahead
                    // LDR PC, [PC, #-4]
                    detour.Method.Write(ref offs, (byte) 0x04);
                    detour.Method.Write(ref offs, (byte) 0xF0);
                    detour.Method.Write(ref offs, (byte) 0x1F);
                    detour.Method.Write(ref offs, (byte) 0xE5);
                    // <to>
                    detour.Method.Write(ref offs, (uint) detour.Target);
                    break;

                case DetourType.AArch32X:
                    // Burn a register to stay safe.
                    // Note: PC is 4 bytes ahead, PC == R15
                    // LDR R14, [PC, #0]
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0xC0);
                    detour.Method.Write(ref offs, (byte) 0x9F);
                    detour.Method.Write(ref offs, (byte) 0xE5);
                    // BX R14
                    detour.Method.Write(ref offs, (byte) 0x1C);
                    detour.Method.Write(ref offs, (byte) 0xFF);
                    detour.Method.Write(ref offs, (byte) 0x2F);
                    detour.Method.Write(ref offs, (byte) 0xE1);
                    // <to>
                    detour.Method.Write(ref offs, (uint) detour.Target);
                    break;

                case DetourType.AArch64:
                    // FIXME: This fails on mono.
                    // PC isn't available on arm64.
                    // We need to burn a register and branch instead.
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
                    throw new NotSupportedException($"Unknown detour type {detour.Type}");
            }
        }

        public void Copy(IntPtr src, IntPtr dst, byte type) {
            switch ((DetourType) type) {
                case DetourType.Thumb:
                    // TODO: Short range Thumb branch using B
                case DetourType.ThumbX:
                    *(ushort*) ((ulong) dst) = *(ushort*) ((ulong) src);
                    *(ushort*) ((ulong) dst + 2) = *(ushort*) ((ulong) src + 2);
                    *(uint*) ((ulong) dst + 4) = *(uint*) ((ulong) src + 4);
                    break;

                case DetourType.AArch32:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(uint*) ((ulong) dst + 4) = *(uint*) ((ulong) src + 4);
                    break;

                case DetourType.AArch32X:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(uint*) ((ulong) dst + 4) = *(uint*) ((ulong) src + 4);
                    *(uint*) ((ulong) dst + 8) = *(uint*) ((ulong) src + 8);
                    break;

                case DetourType.AArch64:
                    *(uint*) ((ulong) dst) = *(uint*) ((ulong) src);
                    *(uint*) ((ulong) dst + 4) = *(uint*) ((ulong) src + 4);
                    *(ulong*) ((ulong) dst + 8) = *(ulong*) ((ulong) src + 8);
                    break;

                default:
                    throw new NotSupportedException($"Unknown detour type {type}");
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
}
