using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms.Architectures {
    internal class x86_64Arch : IArchitecture {
        public ArchitectureKind Target => ArchitectureKind.x86_64;

        public ArchitectureFeature Features => ArchitectureFeature.Immediate64;

        private BytePatternCollection? lazyKnownMethodThunks;
        public unsafe BytePatternCollection KnownMethodThunks => Helpers.GetOrInit(ref lazyKnownMethodThunks, &CreateKnownMethodThunks);
        
        private static BytePatternCollection CreateKnownMethodThunks()
        {
            const ushort An = BytePattern.SAnyValue;
            const ushort Ad = BytePattern.SAddressValue;
            const ushort Ar = BytePattern.SAnyRepeatingValue;
            const byte Bn = BytePattern.BAnyValue;
            const byte Bd = BytePattern.BAddressValue;
            const byte Br = BytePattern.BAnyRepeatingValue;

            if (PlatformDetection.Runtime is RuntimeKind.Framework or RuntimeKind.CoreCLR) {
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

                    null
                );
            } else {
                // TODO: Mono
                return new();
            }
        }

        private sealed class Abs64Kind : DetourKindBase {
            public static readonly Abs64Kind Instance = new();

            public override int Size => 1 + 1 + 4 + 8;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle) {
                buffer[0] = 0xff;
                buffer[1] = 0x25;
                Unsafe.WriteUnaligned(ref buffer[2], (int) 0);
                Unsafe.WriteUnaligned(ref buffer[6], (long) to);
                allocHandle = null;
                return Size;
            }

            public override bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo) {
                // we can always trivially retarget an abs64 detour (change the absolute constant)
                retargetInfo = orig with { To = to };
                return true;
            }


            public override int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
                out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc) {
                needsRepatch = true;
                disposeOldAlloc = true;
                // the retarget logic for rel32 is just the same as the normal patch
                // the patcher should repatch the target method with the new bytes, and dispose the old allocation, if present
                return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
            }
        }

        private sealed class Rel32Ind64Kind : DetourKindBase {
            public static readonly Rel32Ind64Kind Instance = new();

            public override int Size => 1 + 1 + 4;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle) {
                Helpers.ThrowIfArgumentNull(data);
                var alloc = (IAllocatedMemory) data;

                buffer[0] = 0xff;
                buffer[1] = 0x25;
                Unsafe.WriteUnaligned(ref buffer[2], (int) (alloc.BaseAddress - ((nint) from + 6)));

                Unsafe.WriteUnaligned(ref alloc.Memory[0], to);

                allocHandle = alloc;
                return Size;
            }

            public override bool TryGetRetargetInfo(NativeDetourInfo orig, IntPtr to, int maxSize, out NativeDetourInfo retargetInfo) {
                // we can always trivially retarget a rel32ind64 detour (change the value in the indirection cell)
                retargetInfo = orig with { To = to };
                return true;
            }

            public override int DoRetarget(NativeDetourInfo origInfo, IntPtr to, Span<byte> buffer, object? data,
                out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc) {

                if (origInfo.InternalKind == this) {
                    needsRepatch = false;
                    disposeOldAlloc = false;
                    // retarget logic here is trivial, as we will simply be writing the new to into the existing memory allocation
                    Helpers.ThrowIfArgumentNull(data);
                    var alloc = (IAllocatedMemory) data;

                    Unsafe.WriteUnaligned(ref alloc.Memory[0], to);

                    allocationHandle = alloc;
                    return 0; // no repatch is needed 
                } else {
                    needsRepatch = true;
                    disposeOldAlloc = true;
                    // we're retargeting from something else, so need full repatch
                    return GetBytes(origInfo.From, to, buffer, data, out allocationHandle);
                }
            }
        }

        private readonly ISystem system;

        public x86_64Arch(ISystem system) {
            this.system = system;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the allocation is transferred correctly.")]
        public NativeDetourInfo ComputeDetourInfo(nint from, nint to, int sizeHint) {
            x86Shared.FixSizeHint(ref sizeHint);

            if (x86Shared.TryRel32Detour(from, to, sizeHint, out var rel32Info))
                return rel32Info;

            var target = from + 6;
            var lowBound = target + int.MinValue;
            if ((nuint) lowBound > (nuint) target) lowBound = 0;
            var highBound = target + int.MaxValue;
            if ((nuint) highBound < (nuint) target) highBound = -1;
            var memRequest = new PositionedAllocationRequest((nint)target, (nint)lowBound, (nint)highBound, new(IntPtr.Size));
            if (sizeHint >= Rel32Ind64Kind.Instance.Size && system.MemoryAllocator.TryAllocateInRange(memRequest, out var allocated)) {
                return new(from, to, Rel32Ind64Kind.Instance, allocated);
            }

            // TODO: more, smaller detours

            if (sizeHint < Abs64Kind.Instance.Size) {
                MMDbgLog.Warning($"Size too small for all known detour kinds; defaulting to Abs64. provided size: {sizeHint}");
            }
            return new(from, to, Abs64Kind.Instance, null);
        }

        public int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocHandle) {
            return DetourKindBase.GetDetourBytes(info, buffer, out allocHandle);
        }

        public NativeDetourInfo ComputeRetargetInfo(NativeDetourInfo detour, IntPtr to, int maxSizeHint = -1) {
            x86Shared.FixSizeHint(ref maxSizeHint);
            if (DetourKindBase.TryFindRetargetInfo(detour, to, maxSizeHint, out var retarget)) {
                // the detour knows how to retarget itself, we'll use that
                return retarget;
            } else {
                // the detour doesn't know how to retarget itself, lets just compute a new detour to our new target
                return ComputeDetourInfo(detour.From, to, maxSizeHint);
            }
        }

        public int GetRetargetBytes(NativeDetourInfo original, NativeDetourInfo retarget, Span<byte> buffer,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc) {
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

        public unsafe ReadOnlyMemory<IAllocatedMemory> CreateNativeVtableProxyStubs(IntPtr vtableBase, int vtableSize) {
            var os = PlatformDetection.OS;

            ReadOnlySpan<byte> stubData;
            int indexOffs;
            bool premulOffset;

            if (os.Is(OSKind.Windows)) {
                stubData = VtblProxyStubWin;
                indexOffs = VtblProxyStubIdxOffs;
                premulOffset = VtblProxyStubIdxPremul;
            } else {
                // I believe all of the other platforms .NET Core suports uses the System V calling convention
                stubData = VtblProxyStubSysV;
                indexOffs = VtblProxyStubIdxOffs;
                premulOffset = VtblProxyStubIdxPremul;
            }

            var maxAllocSize = system.MemoryAllocator.MaxSize;
            var allStubsSize = stubData.Length * vtableSize;
            var numMainAllocs = allStubsSize / maxAllocSize;

            var numPerAlloc = maxAllocSize / stubData.Length;
            var mainAllocSize = numPerAlloc * stubData.Length;
            var lastAllocSize = allStubsSize % mainAllocSize;
            Helpers.DAssert(mainAllocSize > lastAllocSize);

            var allocs = new IAllocatedMemory[numMainAllocs + (lastAllocSize != 0 ? 1 : 0)];

            var mainAllocArr = ArrayPool<byte>.Shared.Rent(mainAllocSize);
            var mainAllocBuf = mainAllocArr.AsSpan().Slice(0, mainAllocSize);

            // we want to fill the buffer once, then for each alloc, only set the indicies
            for (var i = 0; i < numPerAlloc; i++) {
                stubData.CopyTo(mainAllocBuf.Slice(i * stubData.Length));
            }

            ref var vtblBase = ref Unsafe.AsRef<IntPtr>((void*) vtableBase);

            // now we want to start making our allocations and filling the input vtable pointer
            // we will be using the same alloc request for all of them
            var allocReq = new AllocationRequest(mainAllocSize) {
                Alignment = IntPtr.Size,
                Executable = true
            };

            for (var i = 0; i < numMainAllocs; i++) {
                Helpers.Assert(system.MemoryAllocator.TryAllocate(allocReq, out var alloc));
                allocs[i] = alloc;

                // fill the indicies appropriately
                FillBufferIndicies(stubData.Length, indexOffs, numPerAlloc, i, mainAllocBuf, premulOffset);
                FillVtbl(stubData.Length, numPerAlloc * i, ref vtblBase, numPerAlloc, alloc.BaseAddress);

                // patch the alloc to contain our data
                system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, mainAllocBuf, default);
            }

            // now, if we need one final alloc, do that
            if (lastAllocSize > 0) {
                allocReq = allocReq with { Size = lastAllocSize };

                Helpers.Assert(system.MemoryAllocator.TryAllocate(allocReq, out var alloc));
                allocs[allocs.Length - 1] = alloc;

                // fill the indicies appropriately
                FillBufferIndicies(stubData.Length, indexOffs, numPerAlloc, numMainAllocs, mainAllocBuf, premulOffset);
                FillVtbl(stubData.Length, numPerAlloc * numMainAllocs, ref vtblBase, lastAllocSize / stubData.Length, alloc.BaseAddress);

                // patch the alloc to contain our data
                system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, mainAllocBuf.Slice(0, lastAllocSize), default);
            }

            ArrayPool<byte>.Shared.Return(mainAllocArr);

            return allocs;

            static void FillBufferIndicies(int stubSize, int indexOffs, int numPerAlloc, int i, Span<byte> mainAllocBuf, bool premul) {
                for (var j = 0; j < numPerAlloc; j++) {
                    ref var indexBase = ref mainAllocBuf[j * stubSize + indexOffs];
                    var index = (uint) (numPerAlloc * i + j);
                    if (premul) {
                        index *= (uint) IntPtr.Size;
                    }
                    Unsafe.WriteUnaligned(ref indexBase, index);
                }
            }

            static void FillVtbl(int stubSize, int baseIndex, ref IntPtr vtblBase, int numEntries, nint baseAddr) {
                for (var i = 0; i < numEntries; i++) {
                    Unsafe.Add(ref vtblBase, baseIndex + i) = baseAddr + stubSize * i;
                }
            }
        }
    }
}
