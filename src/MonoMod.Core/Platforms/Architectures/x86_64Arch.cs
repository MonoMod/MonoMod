using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms.Architectures {
    internal class x86_64Arch : IArchitecture {
        public ArchitectureKind Target => ArchitectureKind.x86_64;

        public ArchitectureFeature Features => ArchitectureFeature.Immediate64;

        // -3 is addr, -2 is any repeating, -1 is any
        private BytePatternCollection? lazyKnownMethodThunks;
        public unsafe BytePatternCollection KnownMethodThunks => Helpers.GetOrInit(ref lazyKnownMethodThunks, /*&*/CreateKnownMethodThunks);
        
        private static BytePatternCollection CreateKnownMethodThunks()
        {
            const ushort Sn = BytePattern.SAnyValue;
            const ushort Sd = BytePattern.SAddressValue;
            const byte An = BytePattern.BAnyValue;
            const byte Ad = BytePattern.BAddressValue;

            if (PlatformDetection.Runtime is RuntimeKind.Framework or RuntimeKind.CoreCLR) {
                return new BytePatternCollection(
                    // .NET Framework
                    new(new(AddressKind.Abs64),
                        // test rcx, rcx
                        0x48, 0x85, 0xc9,
                        // je .... ???
                        0x74, Sn,
                        // mov rax, [rcx]
                        0x48, 0x8b, 0x01,
                        // mov ... (extra)
                        0x49, Sn, Sn, Sn, Sn, Sn, Sn, Sn, Sn, Sn,
                        // cmp rax, r10
                        0x49, 0x3b, 0xc2,
                        // je ...
                        0x74, Sn,
                        // mov {TARGET}
                        0x48, 0xb8, Sd, Sd, Sd, Sd, Sd, Sd, Sd, Sd),

                    // .NET Core
                    new(new(AddressKind.Rel32, 5),
                        // jmp {DELTA}
                        0xe9, Sd, Sd, Sd, Sd,
                        // pop rdi
                        0x5f),

                    // Wine wierdness
                    (PlatformDetection.OS.Is(OSKind.Wine)
                        ? new(new(AddressKind.Abs64),
                            // movabs rax, {PTR}
                            0x48, 0xb8, Sd, Sd, Sd, Sd, Sd, Sd, Sd, Sd,
                            // jmp rax
                            0xff, 0xe0)
                        : null),

                    // Autoscan funkyness
                    new(new(AddressKind.Rel32, 19),
                        new byte[] { // mask
                            0xf0, 0xff, 00, 00, 00, 00, 00, 00, 00, 00, 
                            0xff, 0xff, 0xf0, 
                            0xff, 0xff, 00, 00, 00, 00
                        },
                        new byte[] { // pattern
                            // movabs ??1, ???
                            0x40, 0xb8, An, An, An, An, An, An, An, An,
                            // dec WORD PTR [??1]
                            0x66, 0xff, 0x00, 
                            // jne {DELTA}
                            0x0f, 0x85, Ad, Ad, Ad, Ad
                        }), 

                    // PrecodeFixupThunk
                    new(new(AddressKind.PrecodeFixupThunk_Rel32, 5),
                        // call {PRECODE FIXUP THUNK}
                        0xe8, Sd, Sd, Sd, Sd,
                        // pop rsi(?) (is this even consistent?)
                        0x5e)
                );
            } else {
                // TODO: Mono
                return new();
            }
        }

        public enum DetourKind {
            Rel32,
            Abs64,
        }
        private static readonly int[] DetourSizes = {
            1 + 4,
            1 + 1 + 4 + 8,
        };

        private static bool Is32Bit(long to)
            // JMP rel32 is "sign extended to 64-bits"
            => (((ulong) to) & 0x000000007FFFFFFFUL) == ((ulong) to);

        public NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr to) {
            static DetourKind GetDetourKind(IntPtr from, IntPtr to) {
                long rel = (long) to - ((long) from + 5);

                if (Is32Bit(rel) || Is32Bit(-rel)) {
                    unsafe {
                        if (*((byte*) from + 5) != 0x5f) // because Rel32 uses an E9 jump, the byte that would be immediately following the jump
                            return DetourKind.Rel32;     //   must not be 0x5f, otherwise it would be picked up by the matcher on line 39
                    }
                }

                return DetourKind.Abs64;
            }

            var kind = GetDetourKind(from, to);

            return new(from, to, DetourSizes[(int) kind], (int) kind);
        }

        public int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer) {
            if (buffer.Length < info.Size)
                throw new ArgumentException("Buffer too short", nameof(buffer));
            if (DetourSizes[info.InternalKind] != info.Size)
                throw new ArgumentException("Incorrect detour size", nameof(info));

            var kind = (DetourKind) info.InternalKind;

            switch (kind) {
                case DetourKind.Rel32:
                    buffer[0] = 0xe9;
                    Unsafe.WriteUnaligned(ref buffer[1], (int) ((long) info.To - ((long) info.From + 5)));
                    break;
                case DetourKind.Abs64:
                    buffer[0] = 0xff;
                    buffer[1] = 0x25;
                    Unsafe.WriteUnaligned(ref buffer[2], (int) 0);
                    Unsafe.WriteUnaligned(ref buffer[6], (long) info.To);
                    break;

                default:
                    throw new ArgumentException("Invalid detour kind", nameof(info));
            }

            return DetourSizes[info.InternalKind];
        }
    }
}
