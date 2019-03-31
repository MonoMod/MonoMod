using MonoMod.Utils;
using System;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour.Platforms {
    public unsafe sealed class DetourNativeARMPlatform : IDetourNativePlatform {
        // TODO: Make use of possibly shorter near branches.
        public enum DetourType : byte {
            Thumb,
            ThumbBX,
            AArch32,
            AArch32BX,
            AArch64
        }
        private static readonly uint[] DetourSizes = {
            4 + 4,
            4 + 2 + 2 + 4,
            4 + 4,
            4 + 4 + 4,
            4 + 4 + 8
        };

        private static DetourType GetDetourType(IntPtr from, IntPtr to) {
            if (IntPtr.Size >= 8)
                return DetourType.AArch64;

            // The lowest bit is set for Thumb, unset for ARM.
            bool fromThumb = ((long) from & 0x1) == 0x1;
            bool toThumb = ((long) from & 0x1) == 0x1;
            if (fromThumb) {
                if (toThumb) {
                    return DetourType.Thumb;
                } else {
                    return DetourType.ThumbBX;
                }
            } else {
                if (toThumb) {
                    return DetourType.AArch32BX;
                } else {
                    return DetourType.AArch32;
                }
            }
        }

        public NativeDetourData Create(IntPtr from, IntPtr to, byte? type) {
            NativeDetourData detour = new NativeDetourData {
                Method = (IntPtr) ((long) from & ~0x1),
                Target = (IntPtr) ((long) to & ~0x1)
            };
            detour.Size = DetourSizes[detour.Type = type ?? (byte) GetDetourType(from, to)];
            // Console.WriteLine($"{nameof(DetourNativeARMPlatform)} create: {(DetourType) detour.Type} 0x{detour.Method.ToString("X16")} + 0x{detour.Size.ToString("X8")} -> 0x{detour.Target.ToString("X16")}");
            return detour;
        }

        public void Free(NativeDetourData detour) {
            // No extra data.
        }

        public void Apply(NativeDetourData detour) {
            int offs = 0;

            // Console.WriteLine($"{nameof(DetourNativeARMPlatform)} apply: {(DetourType) detour.Type} 0x{detour.Method.ToString("X16")} -> 0x{detour.Target.ToString("X16")}");
            switch ((DetourType) detour.Type) {
                case DetourType.Thumb:
                    // Note: PC is 4 bytes ahead
                    // LDR.W PC, [PC, #0]
                    detour.Method.Write(ref offs, (byte) 0xDF);
                    detour.Method.Write(ref offs, (byte) 0xF8);
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0xF0);
                    // <to> | 0x1 (-> Thumb)
                    detour.Method.Write(ref offs, (uint) detour.Target | 0x1);
                    break;

                case DetourType.ThumbBX:
                    // FIXME: This fails on dotnet for arm running on aarch64.
                    // Burn a register to stay safe.
                    // Note: PC is 4 bytes ahead
                    // LDR.W R10, [PC, #4]
                    detour.Method.Write(ref offs, (byte) 0xDF);
                    detour.Method.Write(ref offs, (byte) 0xF8);
                    detour.Method.Write(ref offs, (byte) 0x04);
                    detour.Method.Write(ref offs, (byte) 0xA0);
                    // BX R10
                    detour.Method.Write(ref offs, (byte) 0x50);
                    detour.Method.Write(ref offs, (byte) 0x47);
                    // NOP
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0xBF);
                    // <to> | 0x0 (-> ARM)
                    detour.Method.Write(ref offs, (uint) detour.Target | 0x0);
                    break;

                case DetourType.AArch32:
                    // FIXME: This was never tested.
                    // Note: PC is 8 bytes ahead
                    // LDR PC, [PC, #-4]
                    detour.Method.Write(ref offs, (byte) 0x04);
                    detour.Method.Write(ref offs, (byte) 0xF0);
                    detour.Method.Write(ref offs, (byte) 0x1F);
                    detour.Method.Write(ref offs, (byte) 0xE5);
                    // <to> | 0x0 (-> ARM)
                    detour.Method.Write(ref offs, (uint) detour.Target | 0x0);
                    break;

                case DetourType.AArch32BX:
                    // FIXME: This was never tested.
                    // Burn a register. Required to use BX to change state.
                    // Note: PC is 4 bytes ahead
                    // LDR R8, [PC, #0]
                    detour.Method.Write(ref offs, (byte) 0x00);
                    detour.Method.Write(ref offs, (byte) 0x80);
                    detour.Method.Write(ref offs, (byte) 0x9F);
                    detour.Method.Write(ref offs, (byte) 0xE5);
                    // BX R8
                    detour.Method.Write(ref offs, (byte) 0x18);
                    detour.Method.Write(ref offs, (byte) 0xFF);
                    detour.Method.Write(ref offs, (byte) 0x2F);
                    detour.Method.Write(ref offs, (byte) 0xE1);
                    // <to> | 0x1 (-> Thumb)
                    detour.Method.Write(ref offs, (uint) detour.Target | 0x1);
                    break;

                case DetourType.AArch64:
                    // PC isn't available on arm64.
                    // We need to burn a register and branch instead.
                    // LDR X15, .+8
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
                    *(uint*) ((long) dst) = *(uint*) ((long) src);
                    *(uint*) ((long) dst + 4) = *(uint*) ((long) src + 4);
                    break;

                case DetourType.ThumbBX:
                    *(uint*) ((long) dst) = *(uint*) ((long) src);
                    *(ushort*) ((long) dst + 4) = *(ushort*) ((long) src + 4);
                    *(ushort*) ((long) dst + 6) = *(ushort*) ((long) src + 6);
                    *(uint*) ((long) dst + 8) = *(uint*) ((long) src + 8);
                    break;

                case DetourType.AArch32:
                    *(uint*) ((long) dst) = *(uint*) ((long) src);
                    *(uint*) ((long) dst + 4) = *(uint*) ((long) src + 4);
                    break;

                case DetourType.AArch32BX:
                    *(uint*) ((long) dst) = *(uint*) ((long) src);
                    *(uint*) ((long) dst + 4) = *(uint*) ((long) src + 4);
                    *(uint*) ((long) dst + 8) = *(uint*) ((long) src + 8);
                    break;

                case DetourType.AArch64:
                    *(uint*) ((long) dst) = *(uint*) ((long) src);
                    *(uint*) ((long) dst + 4) = *(uint*) ((long) src + 4);
                    *(ulong*) ((long) dst + 8) = *(ulong*) ((long) src + 8);
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

        public IntPtr MemAlloc(uint size) {
            return Marshal.AllocHGlobal((int) size);
        }

        public void MemFree(IntPtr ptr) {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
