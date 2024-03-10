using MonoMod.Core.Platforms.Architectures.AltEntryFactories;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms.Architectures
{
    internal sealed class x86_64Arch : IArchitecture
    {
        public ArchitectureKind Target => ArchitectureKind.x86_64;

        public ArchitectureFeature Features => ArchitectureFeature.Immediate64 | ArchitectureFeature.CreateAltEntryPoint;

        private BytePatternCollection? lazyKnownMethodThunks;
        public unsafe BytePatternCollection KnownMethodThunks => Helpers.GetOrInit(ref lazyKnownMethodThunks, &CreateKnownMethodThunks);

        public IAltEntryFactory AltEntryFactory { get; }

        private static BytePatternCollection CreateKnownMethodThunks()
        {
            const ushort An = BytePattern.SAnyValue;
            const ushort Ad = BytePattern.SAddressValue;
            const byte Bn = BytePattern.BAnyValue;
            const byte Bd = BytePattern.BAddressValue;

            if (PlatformDetection.Runtime is RuntimeKind.Framework or RuntimeKind.CoreCLR)
            {
                return new BytePatternCollection(
                    // .NET Framework remoting method stub
                    new(new(AddressKind.Abs64), mustMatchAtStart: true,
                        // test rcx, rcx
                        0x48, 0x85, 0xc9,
                        // je .... ???
                        0x74, An,
                        // mov rax, [rcx]
                        0x48, 0x8b, 0x01,
                        // mov ... (extra)
                        0x49, An, An, An, An, An, An, An, An, An,
                        // cmp rax, r10
                        0x49, 0x3b, 0xc2,
                        // je ...
                        0x74, An,
                        // mov {TARGET}
                        0x48, 0xb8, Ad, Ad, Ad, Ad, Ad, Ad, Ad, Ad),

                    /* the full assembly for the remoting stub is as follows (at least on FX 4.8):
                     * 
                     *   test rcx, rcx
                     *   jz .Lisnull ; null test
                     *   mov rax, qword ptr [rcx] ; load the MT for the object
                     *   movabs r10, TestMT
                     *   cmp rax, r10 ; test for a particular MT
                     *   je .LfoundMT
                     * .Lisnull:
                     *   movabs rax, StableEntryPoint ; this points to the precode of a method entry point
                     *   jmp rax
                     * .LfoundMT:
                     *   movabs r10, ???
                     *   movabs rax, ??? ; a pointer to the remoting dispatch helper
                     *   jmp rax
                     */
                    // the purpose of this stub seems ot be checking if the object is actually of the type, because
                    // if its not, its a transparentproxy, and the remoting code is used instead

                    // .NET Core
                    new(new(AddressKind.Rel32, 5), mustMatchAtStart: true,
                        // jmp {DELTA}
                        0xe9, Ad, Ad, Ad, Ad,
                        // pop rdi
                        0x5f),

                    // Jump stubs and generic context stubs (generic context stubs should match later
                    new(new(AddressKind.Abs64), mustMatchAtStart: false,
                            // movabs r?, {ptr} ; <-- this is for the generic context pointer, for instance
                            // the instruction encoding is REX.W(B) B8+r ..., where the B bit of REX is set if extended 64-bit regs are used
                            //0xfe_48, 0xf8_b8, An, An, An, An, An, An, An, An,
                            // movabs rax, {PTR}
                            0x48, 0xb8, Ad, Ad, Ad, Ad, Ad, Ad, Ad, Ad,
                            // jmp rax
                            0xff, 0xe0),

                    // .NET Core Tiered Compilation thunk
                    new(new(AddressKind.Rel32, 19), mustMatchAtStart: false,
                        new byte[] { // mask
                            0xf0, 0xff, 00, 00, 00, 00, 00, 00, 00, 00,
                            0xff, 0xff, 0xf0,
                            0xff, 0xff, 00, 00, 00, 00
                        },
                        new byte[] { // pattern
                            // movabs ??1, ???
                            0x40, 0xb8, Bn, Bn, Bn, Bn, Bn, Bn, Bn, Bn,
                            // dec WORD PTR [??1]
                            0x66, 0xff, 0x00, 
                            // jne {DELTA}
                            0x0f, 0x85, Bd, Bd, Bd, Bd
                            // TODO: somehow encode a check that the ??1s are the same
                            // I somehow doubt that that's necessary, but hey
                        }),

                    // .NET Core Tiered Compilation thunk, this time with an absolute address
                    new(new(AddressKind.Abs64), mustMatchAtStart: false,
                        new byte[] { // mask
                            0xf0, 0xff, 00, 00, 00, 00, 00, 00, 00, 00,
                            0xff, 0xff, 0xf0,
                            0xff, 00,
                            0xff, 0xff, 00, 00, 00, 00, 00, 00, 00, 00,
                            0xff, 0xff,
                        },
                        new byte[] { // pattern
                            // movabs ??1, ???
                            0x40, 0xb8, Bn, Bn, Bn, Bn, Bn, Bn, Bn, Bn,
                            // dec WORD PTR [??1]
                            0x66, 0xff, 0x00, 
                            // jz need_to_recompile
                            0x74, Bn,
                            // movabs rax, {PTR}
                            0x48, 0xb8, Bd, Bd, Bd, Bd, Bd, Bd, Bd, Bd,
                            // jmp rax
                            0xff, 0xe0,
                            // need_to_recompile: ...

                            // TODO: somehow encode a check that the ??1s are the same
                            // I somehow doubt that that's necessary, but hey
                        }),


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

                    // .NET 7 FixupPrecode main entry point (0x1000 page size)
                    new(new(AddressKind.Rel32 | AddressKind.Indirect, 6), mustMatchAtStart: true,
                        new byte[] { // mask
                            0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                        },
                        new byte[] { // pattern
                            // we know the address will always be 0xfa, 0x0f, 0x00, 0x00, but it's not easy to make the matcher enforce that, so we just won't
                            0xff, 0x25, Bd, Bd, Bd, Bd, // jmp [rip + 0xffa] -> real method body (or if the method hasn't been compiled yet, the next instruction)
                            0x4c, 0x8b, 0x15, 0xfb, 0x0f, 0x00, 0x00, // mov r10, [rip + 0xffb] -> MethodDesc*
                            0xff, 0x25, 0xfd, 0x0f, 0x00, 0x00, // jmp [rip + 0xffd] ; -> PrecodeFixupThunk
                            // padding to fit 24 bytes
                            //0x90, 0x66, 0x66, 0x66, 0x66 // I'm not actually sure if it's safe to match this padding, so I'm not going to
                        }),

                    // These two patterns represent the same CLR datastructure, just at different entry points.

                    // .NET 7 FixupPrecode ThePreStub entry point (0x1000 page size)
                    new(new(AddressKind.PrecodeFixupThunkRel32 | AddressKind.Indirect, 13), mustMatchAtStart: true,
                        new byte[] { // mask
                            //0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                        },
                        new byte[] { // pattern
                            // jmp [rip + 0xffa] -> real method body (or if the method hasn't been compiled yet, the next instruction)
                            0x4c, 0x8b, 0x15, 0xfb, 0x0f, 0x00, 0x00, // mov r10, [rip + 0xffb] -> MethodDesc*
                            // we know the address will always be 0xfd, 0x0f, 0x00, 0x00, but it's not easy to make the matcher enforce that, so we just won't
                            0xff, 0x25, Bd, Bd, Bd, Bd, // jmp [rip + 0xffd] ; -> PrecodeFixupThunk
                            // padding to fit 24 bytes
                            //0x90, 0x66, 0x66, 0x66, 0x66 // I'm not actually sure if it's safe to match this padding, so I'm not going to
                        }),

                    // .NET 7 Call Counting stub (0x1000 page size)
                    new(new(AddressKind.Rel32 | AddressKind.Indirect, 18), mustMatchAtStart: true,
                        new byte[] { // mask
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff,
                            0xff, 0xff,
                            0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                        },
                        new byte[] { // pattern
                            0x48, 0x8b, 0x05, 0xf9, 0x0f, 0x00, 0x00,   // mov rax, qword [rip - 7 + 0x1000]
                            0x66, 0xff, 0x08,                           // dec word [rax]
                            0x74, 0x06,                                 // je +6 (trigger_recomp)
                            // address will always be 0xf6, 0x0f, 0x00, 0x00
                            0xff, 0x25, Bd, Bd, Bd, Bd,                 // jmp [rip - 10 + 0x1000]
                            0xff, 0x25, 0xf8, 0x0f, 0x00, 0x00, // trigger_recomp: jmp [rip - 8 + 0x1000]
                        }),

                    // https://github.com/dotnet/runtime/commit/11a0671c0b8d30740e5f507a077bed87c905acd0#diff-882d8e730c732d6a26346667e364c7c593da321c7c0045345c189d8cd8f82a17
                    // .NET 8 bumped the fixup precode sizes from 4096 (0x1000) to 16384 (0x4000)

                    // .NET 8 FixupPrecode main entry point (0x4000 page size)
                    new(new(AddressKind.Rel32 | AddressKind.Indirect, 6), mustMatchAtStart: true,
                        new byte[] { // mask
                            0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                        },
                        new byte[] { // pattern
                            // we know the address will always be 0xfa, 0x3f, 0x00, 0x00, but it's not easy to make the matcher enforce that, so we just won't
                            0xff, 0x25, Bd, Bd, Bd, Bd, // jmp [rip + 0xffa] -> real method body (or if the method hasn't been compiled yet, the next instruction)
                            0x4c, 0x8b, 0x15, 0xfb, 0x3f, 0x00, 0x00, // mov r10, [rip + 0xffb] -> MethodDesc*
                            0xff, 0x25, 0xfd, 0x3f, 0x00, 0x00, // jmp [rip + 0xffd] ; -> PrecodeFixupThunk
                            // padding to fit 24 bytes
                            //0x90, 0x66, 0x66, 0x66, 0x66 // I'm not actually sure if it's safe to match this padding, so I'm not going to
                        }),

                    // These two patterns represent the same CLR datastructure, just at different entry points.

                    // .NET 8 FixupPrecode ThePreStub entry point (0x4000 page size)
                    new(new(AddressKind.PrecodeFixupThunkRel32 | AddressKind.Indirect, 13), mustMatchAtStart: true,
                        new byte[] { // mask
                            //0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                        },
                        new byte[] { // pattern
                            // jmp [rip + 0x3ffa] -> real method body (or if the method hasn't been compiled yet, the next instruction)
                            0x4c, 0x8b, 0x15, 0xfb, 0x3f, 0x00, 0x00, // mov r10, [rip + 0xffb] -> MethodDesc*
                            // we know the address will always be 0xfd, 0x0f, 0x00, 0x00, but it's not easy to make the matcher enforce that, so we just won't
                            0xff, 0x25, Bd, Bd, Bd, Bd, // jmp [rip + 0x3ffd] ; -> PrecodeFixupThunk
                            // padding to fit 24 bytes
                            //0x90, 0x66, 0x66, 0x66, 0x66 // I'm not actually sure if it's safe to match this padding, so I'm not going to
                        }),

                    // .NET 8 Call Counting stub (0x4000 page size)
                    new(new(AddressKind.Rel32 | AddressKind.Indirect, 18), mustMatchAtStart: true,
                        new byte[] { // mask
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                            0xff, 0xff, 0xff,
                            0xff, 0xff,
                            0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                            0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                        },
                        new byte[] { // pattern
                            0x48, 0x8b, 0x05, 0xf9, 0x3f, 0x00, 0x00,   // mov rax, qword [rip - 7 + 0x4000]
                            0x66, 0xff, 0x08,                           // dec word [rax]
                            0x74, 0x06,                                 // je +6 (trigger_recomp)
                            // address will always be 0xf6, 0x3f, 0x00, 0x00
                            0xff, 0x25, Bd, Bd, Bd, Bd,                 // jmp [rip - 10 + 0x4000]
                            0xff, 0x25, 0xf8, 0x3f, 0x00, 0x00, // trigger_recomp: jmp [rip - 8 + 0x4000]
                        }),

                    null
                );
            }
            else
            {
                // TODO: Mono
                return new();
            }
        }

        private sealed class Abs64Kind : DetourKindBase
        {
            public static readonly Abs64Kind Instance = new();

            public override int Size => 1 + 1 + 4 + 8;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle)
            {
                buffer[0] = 0xff;
                buffer[1] = 0x25;
                Unsafe.WriteUnaligned(ref buffer[2], (int)0);
                Unsafe.WriteUnaligned(ref buffer[6], (long)to);
                allocHandle = null;
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
                // the patcher should repatch the target method with the new bytes, and dispose the old allocation, if present
                return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
            }
        }

        private sealed class Rel32Ind64Kind : DetourKindBase
        {
            public static readonly Rel32Ind64Kind Instance = new();

            public override int Size => 1 + 1 + 4;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle)
            {
                Helpers.ThrowIfArgumentNull(data);
                var alloc = (IAllocatedMemory)data;

                buffer[0] = 0xff;
                buffer[1] = 0x25;
                Unsafe.WriteUnaligned(ref buffer[2], (int)(alloc.BaseAddress - ((nint)from + 6)));

                Unsafe.WriteUnaligned(ref alloc.Memory[0], to);

                allocHandle = alloc;
                return Size;
            }

            public override bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo)
            {
                // we can always trivially retarget a rel32ind64 detour (change the value in the indirection cell)
                retargetInfo = orig with { To = to };
                return true;
            }

            public override int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
                out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc)
            {

                if (origInfo.InternalKind == this)
                {
                    needsRepatch = false;
                    disposeOldAlloc = false;
                    // retarget logic here is trivial, as we will simply be writing the new to into the existing memory allocation
                    Helpers.ThrowIfArgumentNull(data);
                    var alloc = (IAllocatedMemory)data;

                    Unsafe.WriteUnaligned(ref alloc.Memory[0], to);

                    allocationHandle = alloc;
                    return 0; // no repatch is needed 
                }
                else
                {
                    needsRepatch = true;
                    disposeOldAlloc = true;
                    // we're retargeting from something else, so need full repatch
                    return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
                }
            }
        }

        private readonly ISystem system;

        public x86_64Arch(ISystem system)
        {
            this.system = system;
            AltEntryFactory = new IcedAltEntryFactory(system, 64);
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the allocation is transferred correctly.")]
        public NativeDetourInfo ComputeDetourInfo(nint from, nint to, int sizeHint)
        {
            x86Shared.FixSizeHint(ref sizeHint);

            if (x86Shared.TryRel32Detour(from, to, sizeHint, out var rel32Info))
                return rel32Info;

            var target = from + 6;
            var lowBound = target + int.MinValue;
            if ((nuint)lowBound > (nuint)target)
                lowBound = 0;
            var highBound = target + int.MaxValue;
            if ((nuint)highBound < (nuint)target)
                highBound = -1;
            var memRequest = new PositionedAllocationRequest((nint)target, (nint)lowBound, (nint)highBound, new(IntPtr.Size));
            if (sizeHint >= Rel32Ind64Kind.Instance.Size && system.MemoryAllocator.TryAllocateInRange(memRequest, out var allocated))
            {
                return new(from, to, Rel32Ind64Kind.Instance, allocated);
            }

            // TODO: more, smaller detours

            if (sizeHint < Abs64Kind.Instance.Size)
            {
                MMDbgLog.Warning($"Size too small for all known detour kinds; defaulting to Abs64. provided size: {sizeHint}");
            }
            return new(from, to, Abs64Kind.Instance, null);
        }

        public int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocHandle)
        {
            return DetourKindBase.GetDetourBytes(info, buffer, out allocHandle);
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

        private const int VtblProxyStubIdxOffs = 0x9;
        private const bool VtblProxyStubIdxPremul = true;
        private static ReadOnlySpan<byte> VtblProxyStubWin => new byte[] {
            0x48, 0x8B, 0x49, 0x08, 0x48, 0x8B, 0x01, 0xFF, 0xA0, 0x55, 0x55, 0x55, 0x55, 0xCC, 0xCC, 0xCC
        };
        private static ReadOnlySpan<byte> VtblProxyStubSysV => new byte[] {
            0x48, 0x8B, 0x7F, 0x08, 0x48, 0x8B, 0x07, 0xFF, 0xA0, 0x55, 0x55, 0x55, 0x55, 0xCC, 0xCC, 0xCC
        };

        public ReadOnlyMemory<IAllocatedMemory> CreateNativeVtableProxyStubs(IntPtr vtableBase, int vtableSize)
        {
            var os = PlatformDetection.OS;

            ReadOnlySpan<byte> stubData;
            int indexOffs;
            bool premulOffset;

            if (os.Is(OSKind.Windows))
            {
                stubData = VtblProxyStubWin;
                indexOffs = VtblProxyStubIdxOffs;
                premulOffset = VtblProxyStubIdxPremul;
            }
            else
            {
                // I believe all of the other platforms .NET Core suports uses the System V calling convention
                stubData = VtblProxyStubSysV;
                indexOffs = VtblProxyStubIdxOffs;
                premulOffset = VtblProxyStubIdxPremul;
            }

            return Shared.CreateVtableStubs(system, vtableBase, vtableSize, stubData, indexOffs, premulOffset);
        }

        private const int SpecEntryStubArgOffs = 2;
        private const int SpecEntryStubTargetOffs = 0xC;
        private static ReadOnlySpan<byte> SpecEntryStub => new byte[] {
            0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x49, 0xBA, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x41, 0xFF, 0xE2
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
