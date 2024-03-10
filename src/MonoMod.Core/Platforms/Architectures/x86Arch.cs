using MonoMod.Core.Platforms.Architectures.AltEntryFactories;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures
{
    internal sealed class x86Arch : IArchitecture
    {
        public ArchitectureKind Target => ArchitectureKind.x86;
        public ArchitectureFeature Features => ArchitectureFeature.CreateAltEntryPoint;

        private BytePatternCollection? lazyKnownMethodThunks;
        public unsafe BytePatternCollection KnownMethodThunks => Helpers.GetOrInit(ref lazyKnownMethodThunks, &CreateKnownMethodThunks);

        public IAltEntryFactory AltEntryFactory { get; }

        private static BytePatternCollection CreateKnownMethodThunks()
        {
            const ushort An = BytePattern.SAnyValue;
            const ushort Ad = BytePattern.SAddressValue;
            //const byte Bn = BytePattern.BAnyValue;
            //const byte Bd = BytePattern.BAddressValue;

            if (PlatformDetection.Runtime is RuntimeKind.Framework or RuntimeKind.CoreCLR)
            {
                return new BytePatternCollection(
                    // .NET Framework
                    new(new(AddressKind.Rel32, 0x10), // UNKNOWN mustMatchAtStart
                                                      // mov ... (mscorlib_ni!???)
                        0xb8, An, An, An, An,
                        // nop
                        0x90,
                        // call ... (clr!PrecodeRemotingThunk)
                        0xe8, An, An, An, An,
                        // jmp {DELTA}
                        0xe9, Ad, Ad, Ad, Ad),

                    // .NET Core
                    new(new(AddressKind.Rel32, 5), mustMatchAtStart: true,
                        // jmp {DELTA}
                        0xe9, Ad, Ad, Ad, Ad,
                        // pop rdi
                        0x5f),

                    // PrecodeFixupThunk (CLR 4+)
                    new(new(AddressKind.PrecodeFixupThunkRel32, 5), mustMatchAtStart: true,
                        // call {PRECODE FIXUP THUNK}
                        0xe8, Ad, Ad, Ad, Ad,
                        // pop rsi(?) (is this even consistent?)
                        0x5e),

                    // PrecodeFixupThunk (CLR 2)
                    new(new(AddressKind.PrecodeFixupThunkRel32, 5), mustMatchAtStart: true,
                        // call {PRECODE FIXUP THUNK}
                        0xe8, Ad, Ad, Ad, Ad,
                        // int 3
                        0xcc),

                    // .NET 7 FixupPrecode (main entry point)
                    new(new(AddressKind.Abs32 | AddressKind.Indirect), mustMatchAtStart: true,
                        // jmp dword [pTarget]
                        0xff, 0x25, Ad, Ad, Ad, Ad,
                        // mov eax, dword [pMethodDesc]
                        0xa1, An, An, An, An,
                        // jmp dword [pPrecodeFixupThunk]
                        0xff, 0x25, An, An, An, An
                    ),

                    // .NET 7 FixupPrecode (precode entry point)
                    new(new(AddressKind.PrecodeFixupThunkAbs32 | AddressKind.Indirect), mustMatchAtStart: true,
                        // mov eax, dword [pMethodDesc]
                        0xa1, An, An, An, An,
                        // jmp dword [pPrecodeFixupThunk]
                        0xff, 0x25, Ad, Ad, Ad, Ad
                    ),

                    null
                );
            }
            else
            {
                // TODO: Mono
                return new();
            }
        }

        private sealed class Abs32Kind : DetourKindBase
        {
            public static readonly Abs32Kind Instance = new();

            public override int Size => 1 + 4 + 1;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle)
            {
                buffer[0] = 0x68; // PUSH imm32
                Unsafe.WriteUnaligned(ref buffer[1], Unsafe.As<IntPtr, int>(ref to));
                buffer[5] = 0xc3; // RET

                allocHandle = null;
                return Size;
            }

            public override bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo)
            {
                // we can always trivially retarget an abs32
                retargetInfo = orig with { To = to };
                return true;
            }

            public override int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
                out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
            {
                needsRepatch = true;
                disposeOldAlloc = true;
                // the retarget logic for rel32 is just the same as the normal patch
                // the patcher should repatch the target method with the new bytes, and dispose the old allocation, if present
                return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
            }
        }

        public NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr to, int maxSizeHint = -1)
        {
            x86Shared.FixSizeHint(ref maxSizeHint);

            if (x86Shared.TryRel32Detour(from, to, maxSizeHint, out var rel32Info))
                return rel32Info;

            if (maxSizeHint < Abs32Kind.Instance.Size)
            {
                MMDbgLog.Warning($"Size too small for all known detour kinds; defaulting to Abs32. provided size: {maxSizeHint}");
            }

            return new(from, to, Abs32Kind.Instance, null);
        }

        public int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocationHandle)
        {
            return DetourKindBase.GetDetourBytes(info, buffer, out allocationHandle);
        }

        public NativeDetourInfo ComputeRetargetInfo(NativeDetourInfo detour, IntPtr to, int maxSizeHint = -1)
        {
            x86Shared.FixSizeHint(ref maxSizeHint);
            if (DetourKindBase.TryFindRetargetInfo(detour, to, maxSizeHint, out var retarget))
            {
                // the detour knows how to retarget itself, we'll use that
                return retarget;
            }
            else
            {
                // the detour doesn't know how to retarget itself, lets just compute a new detour to our new target
                return ComputeDetourInfo(detour.From, to, maxSizeHint);
            }
        }

        public int GetRetargetBytes(NativeDetourInfo original, NativeDetourInfo retarget, Span<byte> buffer,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
        {
            return DetourKindBase.DoRetarget(original, retarget, buffer, out allocationHandle, out needsRepatch, out disposeOldAlloc);
        }

        private readonly ISystem system;
        public x86Arch(ISystem system)
        {
            this.system = system;
            AltEntryFactory = new IcedAltEntryFactory(system, 32);
        }

        private const int WinThisVtableThunkIndexOffs = 0x7;
        private static ReadOnlySpan<byte> WinThisVtableProxyThunk => new byte[] {
            0x8B, 0x49, 0x04, 0x8B, 0x01, 0xFF, 0xA0, 0x55, 0x55, 0x55, 0x55, 0xCC
        };

        public ReadOnlyMemory<IAllocatedMemory> CreateNativeVtableProxyStubs(IntPtr vtableBase, int vtableSize)
        {
            var os = PlatformDetection.OS.GetKernel();

            ReadOnlySpan<byte> stubData;
            int indexOffs;
            var premulOffset = true;

            if (os.Is(OSKind.Windows))
            {
                stubData = WinThisVtableProxyThunk;
                indexOffs = WinThisVtableThunkIndexOffs;
            }
            else
            {
                // x86 is only supported on windows, but we might want this for something on x86 sometime in the future
                throw new PlatformNotSupportedException();
            }

            return Shared.CreateVtableStubs(system, vtableBase, vtableSize, stubData, indexOffs, premulOffset);
        }


        private const int SpecEntryStubArgOffs = 1;
        private const int SpecEntryStubTargetOffs = 6;
        private static ReadOnlySpan<byte> SpecEntryStub => new byte[] {
            0xB8, 0x00, 0x00, 0x00, 0x00, 0xB9, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xE1
        };

        public IAllocatedMemory CreateSpecialEntryStub(IntPtr target, IntPtr argument)
        {
            Span<byte> stub = stackalloc byte[SpecEntryStub.Length];
            SpecEntryStub.CopyTo(stub);
            Unsafe.WriteUnaligned(ref stub[SpecEntryStubTargetOffs], target);
            Unsafe.WriteUnaligned(ref stub[SpecEntryStubArgOffs], argument);
            Helpers.Assert(system.MemoryAllocator.TryAllocate(new(stub.Length) { Executable = true, Alignment = 1 }, out var alloc));
            system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, stub, default);
            return alloc;
        }
    }
}
