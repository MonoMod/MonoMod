using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures {
    internal sealed class x86Arch : IArchitecture {
        public ArchitectureKind Target => ArchitectureKind.x86;

        public ArchitectureFeature Features => ArchitectureFeature.None;

        private BytePatternCollection? lazyKnownMethodThunks;
        public unsafe BytePatternCollection KnownMethodThunks => Helpers.GetOrInit(ref lazyKnownMethodThunks, &CreateKnownMethodThunks);

        private static BytePatternCollection CreateKnownMethodThunks() {
            const ushort An = BytePattern.SAnyValue;
            const ushort Ad = BytePattern.SAddressValue;
            //const byte Bn = BytePattern.BAnyValue;
            //const byte Bd = BytePattern.BAddressValue;

            if (PlatformDetection.Runtime is RuntimeKind.Framework or RuntimeKind.CoreCLR) {
                return new BytePatternCollection(
                    // .NET Framework
                    new(new(AddressKind.Rel32, 0xc),
                        // mov ... (mscorlib_ni!???)
                        0xb8, An, An, An, An,
                        // nop
                        0x90,
                        // call ... (clr!PrecodeRemotingThunk)
                        0xe8, An, An, An, An,
                        // jmp {DELTA}
                        0xe9, Ad, Ad, Ad, Ad),

                    // .NET Core
                    new(new(AddressKind.Rel32, 5),
                        // jmp {DELTA}
                        0xe9, Ad, Ad, Ad, Ad,
                        // pop rdi
                        0x5f),

                    // PrecodeFixupThunk (CLR 4+)
                    new(new(AddressKind.PrecodeFixupThunkRel32, 5),
                        // call {PRECODE FIXUP THUNK}
                        0xe8, Ad, Ad, Ad, Ad,
                        // pop rsi(?) (is this even consistent?)
                        0x5e),

                    // PrecodeFixupThunk (CLR 2)
                    new(new(AddressKind.PrecodeFixupThunkRel32, 5),
                        // call {PRECODE FIXUP THUNK}
                        0xe8, Ad, Ad, Ad, Ad,
                        // int 3
                        0xcc),

                    null
                );
            } else {
                // TODO: Mono
                return new();
            }
        }

        private sealed class Abs32Kind : DetourKindBase {
            public static readonly Abs32Kind Instance = new();

            public override int Size => 1 + 4 + 1;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle) {
                buffer[0] = 0x68; // PUSH imm32
                Unsafe.WriteUnaligned(ref buffer[1], Unsafe.As<IntPtr, int>(ref to));
                buffer[5] = 0xc3; // RET

                allocHandle = null;
                return Size;
            }
        }

        public NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr to, int maxSizeHint = -1) {
            x86Shared.FixSizeHint(ref maxSizeHint);

            if (x86Shared.TryRel32Detour(from, to, maxSizeHint, out var rel32Info))
                return rel32Info;

            if (maxSizeHint < Abs32Kind.Instance.Size) {
                MMDbgLog.Log($"Size too small for all known detour kinds; defaulting to Abs32. provided size: {maxSizeHint}");
            }

            return new(from, to, Abs32Kind.Instance, null);
        }

        public int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocationHandle) {
            return DetourKindBase.GetDetourBytes(info, buffer, out allocationHandle);
        }
    }
}
