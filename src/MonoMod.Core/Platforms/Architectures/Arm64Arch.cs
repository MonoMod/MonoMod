using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms.Architectures
{
    internal sealed class Arm64Arch : IArchitecture
    {
        public ArchitectureKind Target => ArchitectureKind.Arm64;

        public ArchitectureFeature Features => ArchitectureFeature.Immediate64;

        private BytePatternCollection? lazyKnownMethodThunks;
        public BytePatternCollection KnownMethodThunks => Helpers.GetOrInit(ref lazyKnownMethodThunks, CreateKnownMethodThunks);

        public IAltEntryFactory AltEntryFactory => null!;

        private readonly ISystem System;

        public Arm64Arch(ISystem system)
        {
            System = system;
        }

        public NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr target, int maxSizeHint)
        {
            // Should work for arm64 as well
            x86Shared.FixSizeHint(ref maxSizeHint);

            if (maxSizeHint < BranchRegisterKind.Instance.Size)
            {
                MMDbgLog.Warning($"Size too small for all known detour kinds! Defaulting to BranchRegister. provided size: {maxSizeHint}");
            }

            return new(from, target, BranchRegisterKind.Instance, null);
        }

        public int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocationHandle)
        {
            return DetourKindBase.GetDetourBytes(info, buffer, out allocationHandle);
        }

        public NativeDetourInfo ComputeRetargetInfo(NativeDetourInfo detour, IntPtr target, int maxSizeHint = -1)
        {
            // Should work for arm64 as well
            x86Shared.FixSizeHint(ref maxSizeHint);

            if (DetourKindBase.TryFindRetargetInfo(detour, target, maxSizeHint, out var retarget))
            {
                // the detour knows how to retarget itself, we'll use that
                return retarget;
            }

            // the detour doesn't know how to retarget itself, lets just compute a new detour to our new target
            return ComputeDetourInfo(detour.From, target, maxSizeHint);
        }

        public int GetRetargetBytes(NativeDetourInfo original, NativeDetourInfo retarget, Span<byte> buffer,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
        {
            return DetourKindBase.DoRetarget(original, retarget, buffer, out allocationHandle, out needsRepatch, out disposeOldAlloc);
        }

        public ReadOnlyMemory<IAllocatedMemory> CreateNativeVtableProxyStubs(IntPtr vtableBase, int vtableSize)
        {
            ReadOnlySpan<byte> stubData = [
                0x00, 0x04, 0x40, 0xF9, // ldr x0, [x0, #8]
                0x09, 0x00, 0x40, 0xF9, // ldr x9, [x0]
                0x8A, 0x00, 0x00, 0x18, // ldr w10, _offset
                0x29, 0x01, 0x0A, 0x8B, // add x9, x9, x10
                0x29, 0x01, 0x40, 0xF9, // ldr x9, [x9]
                0x20, 0x01, 0x1F, 0xD6, // br x9
                0x00, 0x00, 0x00, 0x00, // _offset: .word 0x0
            ];

            return Shared.CreateVtableStubs(System, vtableBase, vtableSize, stubData, 24, true);
        }

        public IAllocatedMemory CreateSpecialEntryStub(IntPtr target, IntPtr argument)
        {
            ReadOnlySpan<byte> stubData = [
                0x89, 0x00, 0x00, 0x58, // ldr x9, [pc, #16]
                0xaa, 0x00, 0x00, 0x58, // ldr x10, [pc, #24]
                0x40, 0x01, 0x1f, 0xd6, // br x10
                0x1f, 0x20, 0x03, 0xd5, // nop
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            ];

            Span<byte> stub = stackalloc byte[stubData.Length];
            stubData.CopyTo(stub);
            Unsafe.WriteUnaligned(ref stub[16], argument);
            Unsafe.WriteUnaligned(ref stub[24], target);
            return Shared.CreateSingleExecutableStub(System, stub);
        }

        private static BytePatternCollection CreateKnownMethodThunks()
        {
            const byte Bn = BytePattern.BAnyValue;
            const byte Bd = BytePattern.BAddressValue;

            if (PlatformDetection.Runtime is RuntimeKind.Framework or RuntimeKind.CoreCLR)
            {
                var patterns = new List<BytePattern>()
                {
                    // .NET 6 Support
                    //
                    // StubPrecode
                    new BytePattern(new AddressMeaning(AddressKind.Abs64), mustMatchAtStart: true,
                        new byte[]
                        {
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                        },
                        new byte[]
                        {
                            0x89, 0x00, 0x00, 0x10, // adr x9, #16
                            0x2a, 0x31, 0x40, 0xa9, // ldp x10,x12,[x9]      ; =m_pTarget,m_pMethodDesc
                            0x40, 0x01, 0x1f, 0xd6, // br x10
                              Bn,   Bn,   Bn,   Bn,
                              Bd,   Bd,   Bd,   Bd,
                              Bd,   Bd,   Bd,   Bd
                        }
                    ),
                    // NDirectImportPrecode
                    new BytePattern(new AddressMeaning(AddressKind.Abs64), mustMatchAtStart: true,
                        new byte[]
                        {
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                        },
                        new byte[]
                        {
                            0x8b, 0x00, 0x00, 0x10, // adr x11, #16             ; Notice that x11 register is used to differentiate the stub from StubPrecode which uses x9
                            0x6a, 0x31, 0x40, 0xa9, // ldp x10,x12,[x11]      ; =m_pTarget,m_pMethodDesc
                            0x40, 0x01, 0x1f, 0xd6, // br  x10
                              Bn,   Bn,   Bn,   Bn,
                              Bd,   Bd,   Bd,   Bd,
                              Bd,   Bd,   Bd,   Bd
                        }
                    ),
                    // FixupPrecode 
                    new BytePattern(new AddressMeaning(AddressKind.Abs64), mustMatchAtStart: true,
                        new byte[]
                        {
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                        },
                        new byte[]
                        {
                            0x0c, 0x00, 0x00, 0x10, // adr x12, #0
                            0x6b, 0x00, 0x00, 0x58, // ldr x11, [pc, #12]     ; =m_pTarget
                            0x60, 0x01, 0x1f, 0xd6, // br  x11
                              Bn,   Bn,   Bn,   Bn,
                              Bd,   Bd,   Bd,   Bd,
                              Bd,   Bd,   Bd,   Bd
                        }
                    ),
                    // ThisPtrRetBufPrecode
                    new BytePattern(new AddressMeaning(AddressKind.Abs64), mustMatchAtStart: true,
                        new byte[]
                        {
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                        },
                        new byte[]
                        {
                            0x10, 0x00, 0x00, 0x91, // mov x16, x0
                            0x20, 0x00, 0x00, 0x91, // mov x0, x1
                            0x01, 0x02, 0x00, 0x91, // mov x1, x16
                            0x70, 0x00, 0x00, 0x58, // ldr x16, [pc, #12]
                            0x00, 0x02, 0x1f, 0xd6, // br  x16
                              Bn,   Bn,   Bn,   Bn,
                              Bd,   Bd,   Bd,   Bd,
                              Bd,   Bd,   Bd,   Bd
                        }
                    ),
                };

                // .NET 7+ Support
                //
                // The precode helpers are generated for ALL of the page sizes below, and selected between dynamically according to the current system's page size.
                // We can't be sure that whatever we pick up as the system page size (done dynamically) is the same as what the runtime is using here, so we generate
                // patterns for all of them.
                ReadOnlySpan<int> pageSizes = [4096, 8192, 16384, 32768, 65536]; // note: these are defined in src/coreclr/vm/arm64/thunktemplates.S

                // #define DATA_SLOT(stub, field) (stub##Code + STUB_PAGE_SIZE + stub##Data__##field)
                //
                // FixupPrecodeCode
                ReadOnlySpan<byte> fixupPrecodeCode = 
                [
                    0x0b, 0x00, 0x02, 0x58, // ldr x11, DATA_SLOT(FixupPrecode, Target) // +0
                    0x60, 0x01, 0x1f, 0xd6, // br x11
                    0x0c, 0x00, 0x02, 0x58, // ldr x12, DATA_SLOT(FixupPrecode, MethodDesc) // +8
                    0x2b, 0x00, 0x02, 0x58, // ldr x11, DATA_SLOT(FixupPrecode, PrecodeFixupThunk) // +16
                    0x60, 0x01, 0x1f, 0xd6, // br x11
                ];
                //
                // FixupPrecodeCode (.NET 10 thunktemplates.S)
                ReadOnlySpan<byte> fixupPrecodeCode2 = 
                [
                    0x0b, 0x00, 0x02, 0x58, // ldr x11, DATA_SLOT(FixupPrecode, Target) // +0
                    0x60, 0x01, 0x1f, 0xd6, // br x11
                    0xbf, 0x39, 0x03, 0xd5, // dmb ishld
                    0x0c, 0x00, 0x02, 0x58, // ldr x12, DATA_SLOT(FixupPrecode, MethodDesc) // +8
                    0x2b, 0x00, 0x02, 0x58, // ldr x11, DATA_SLOT(FixupPrecode, PrecodeFixupThunk) // +16
                    0x60, 0x01, 0x1f, 0xd6, // br x11
                ];
                // CallCountingStubCode
                ReadOnlySpan<byte> callCountingStubCode =
                [
                    0x09, 0x00, 0x02, 0x58, // ldr  x9, DATA_SLOT(CallCountingStub, RemainingCallCountCell) // +0
                    0x2a, 0x01, 0x40, 0x79, // ldrh w10, [x9]
                    0x4a, 0x05, 0x00, 0x71, // subs w10, w10, #1
                    0x2a, 0x01, 0x00, 0x79, // strh w10, [x9]
                    0x60, 0x00, 0x00, 0x54, // beq CountReachedZero
                    0xa9, 0xff, 0x01, 0x58, // ldr  x9, DATA_SLOT(CallCountingStub, TargetForMethod) // +8
                    0x20, 0x01, 0x1f, 0xd6, // br   x9
                                            // CountReachedZero:
                    0xaa, 0xff, 0x01, 0x58, // ldr  x10, DATA_SLOT(CallCountingStub, TargetForThresholdReached) // +16
                    0x40, 0x01, 0x1F, 0xD6, // br   x10
                ];

                ReadOnlyMemory<byte> bigMask = (byte[])[
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                    0xff, 0xff, 0xff, 0xff,
                ];

                foreach (var pageSize in pageSizes)
                {
                    var fixupPrecode = fixupPrecodeCode.ToArray(); // create a copy to operate on
                    EncodeLdr64LiteralTo(fixupPrecode.AsSpan(0), 0 + pageSize + 0, 11); // .Target (+0)
                    EncodeLdr64LiteralTo(fixupPrecode.AsSpan(8), -8 + pageSize + 8, 12); // .MethodDesc (+8)
                    EncodeLdr64LiteralTo(fixupPrecode.AsSpan(12), -12 + pageSize + 16, 11); // .PrecodeFixupThunk (+16)

                    // we need to generate patterns for both entries
                    // main one first
                    patterns.Add(new BytePattern(
                        new AddressMeaning(AddressKind.Rel64 | AddressKind.ConstantAddr | AddressKind.Indirect, relativeOffset: 0, constantValue: (uint)pageSize + 0),
                        mustMatchAtStart: true,
                        mask: bigMask.Slice(0, fixupPrecode.Length),
                        pattern: fixupPrecode));
                    // then the precode fixup entry
                    patterns.Add(new BytePattern(
                        new AddressMeaning(AddressKind.PrecodeFixupThunkRel64 | AddressKind.ConstantAddr | AddressKind.Indirect, relativeOffset: 0, constantValue: (uint)pageSize + 16 - 8),
                        mustMatchAtStart: true,
                        mask: bigMask.Slice(0, fixupPrecode.Length - 8),
                        pattern: fixupPrecode.AsMemory(8)));

                    var fixupPrecode2 = fixupPrecodeCode2.ToArray(); // create a copy to operate on
                    EncodeLdr64LiteralTo(fixupPrecode2.AsSpan(0), 0 + pageSize + 0, 11); // .Target (+0)
                    EncodeLdr64LiteralTo(fixupPrecode2.AsSpan(12), -12 + pageSize + 8, 12); // .MethodDesc (+18)
                    EncodeLdr64LiteralTo(fixupPrecode2.AsSpan(16), -16 + pageSize + 16, 11); // .PrecodeFixupThunk (+16)

                    // we need to generate patterns for both entries
                    // main one first
                    patterns.Add(new BytePattern(
                        new AddressMeaning(AddressKind.Rel64 | AddressKind.ConstantAddr | AddressKind.Indirect, relativeOffset: 0, constantValue: (uint)pageSize + 0),
                        mustMatchAtStart: true,
                        mask: bigMask.Slice(0, fixupPrecode2.Length),
                        pattern: fixupPrecode2));
                    // then the precode fixup entry
                    patterns.Add(new BytePattern(
                        new AddressMeaning(AddressKind.PrecodeFixupThunkRel64 | AddressKind.ConstantAddr | AddressKind.Indirect, relativeOffset: 0, constantValue: (uint)pageSize + 16 - 8),
                        mustMatchAtStart: true,
                        mask: bigMask.Slice(0, fixupPrecode2.Length - 8),
                        pattern: fixupPrecode2.AsMemory(8)));

                    // then the call counting stub
                    var callCountingStub = callCountingStubCode.ToArray();
                    EncodeLdr64LiteralTo(callCountingStub.AsSpan(0), 0 + pageSize + 0, 9); // .RemainingCallCountCell (+0)
                    EncodeLdr64LiteralTo(callCountingStub.AsSpan(20), -20 + pageSize + 8, 9); // .TargetForMethod (+8)
                    EncodeLdr64LiteralTo(callCountingStub.AsSpan(28), -28 + pageSize + 16, 9); // .TargetForThresholdReached (+16)

                    patterns.Add(new BytePattern(
                        new AddressMeaning(AddressKind.Rel64 | AddressKind.ConstantAddr | AddressKind.Indirect, relativeOffset: 0, constantValue: (uint)pageSize + 8),
                        mustMatchAtStart: true,
                        mask: bigMask.Slice(0, callCountingStub.Length),
                        pattern: callCountingStub));
                }


                return new BytePatternCollection(patterns.ToArray());
            }
            else
            {
                // TODO: Mono
                return new();
            }
        }

        private static void EncodeLdr64LiteralTo(Span<byte> dest, int offset, byte reg)
        {
            Helpers.DAssert(dest.Length >= 4);
            Helpers.DAssert((offset & 0b11) == 0);
            Helpers.DAssert((offset & (~(((1 << 19) - 1) << 2))) == 0 || (-offset & (~(((1 << 19) - 1) << 2))) == 0);
            Helpers.DAssert(reg is >= 0 and <= 30);

            var imm19 = unchecked((uint)offset) >> 2;
            imm19 &= (1 << 19) - 1;

            uint opcode = 0x58000000;
            opcode |= imm19 << 5;
            opcode |= reg;

            MemoryMarshal.Write(dest, ref opcode);
        }

        private sealed class BranchRegisterKind : DetourKindBase
        {
            public static readonly BranchRegisterKind Instance = new();

            public override int Size => 4 + 4 + 8;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle)
            {
                ReadOnlySpan<byte> stubData = [
                    0x49, 0x00, 0x00, 0x58, // ldr x9, _target
                    0x20, 0x01, 0x1F, 0xD6, // br x9
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // _target: .quad 0x0
                ];

                stubData.CopyTo(buffer);
                Unsafe.WriteUnaligned(ref buffer[8], (ulong)to);

                allocHandle = null;
                
                MMDbgLog.Trace($"Detouring arm64 from 0x{from:X16} to 0x{to:X16}");

                return Size;
            }

            public override bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo)
            {
                // we can always trivially retarget an abs64 detour (change the absolute constant)
                retargetInfo = orig with { To = to };
                return true;
            }


            public override int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
                out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
            {
                needsRepatch = true;
                disposeOldAlloc = true;
                // the retarget logic for rel32 is just the same as the normal patch
                // the patcher should re-patch the target method with the new bytes, and dispose the old allocation, if present
                return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
            }
        }
    }
}
