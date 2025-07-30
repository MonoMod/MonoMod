using MonoMod.Utils;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Platforms.Architectures
{
    internal static class Shared
    {
        public static unsafe IAllocatedMemory CreateSingleExecutableStub(ISystem system, ReadOnlySpan<byte> stubBytes)
        {
            var cfg = system as IControlFlowGuard;
            if (cfg is { IsSupported: false }) cfg = null;

            Helpers.Assert(system.MemoryAllocator.TryAllocate(new(stubBytes.Length)
            {
                Executable = true,
                Alignment = cfg is not null ? cfg.TargetAlignmentRequirement : 1, // if CFG is supported, use that alignment
            }, out var alloc));
            system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, stubBytes, default);

            if (cfg is not null)
            {
                unsafe
                {
                    // if CFG is available, make sure we mark the allocation as such
                    cfg.RegisterValidIndirectCallTargets(
                        (void*)alloc.BaseAddress, alloc.Size,
                        [0]);
                }
            }

            return alloc;
        }

        public static unsafe ReadOnlyMemory<IAllocatedMemory> CreateVtableStubs(ISystem system, IntPtr vtableBase, int vtableSize, ReadOnlySpan<byte> stubData, int indexOffs, bool premulOffset)
        {
            var cfg = system as IControlFlowGuard;
            if (cfg is { IsSupported: false }) cfg = null;

            var stubSize = stubData.Length;
            if (cfg is not null)
            {
                var requiredAlign = cfg.TargetAlignmentRequirement;
                // we need to expand the size allocated for each stub to ensure it meets the alignment requirement
                stubSize = (((stubSize - 1) / requiredAlign) + 1) * requiredAlign;
            }

            var maxAllocSize = system.MemoryAllocator.MaxSize;
            var allStubsSize = stubSize * vtableSize;
            var numMainAllocs = allStubsSize / maxAllocSize;

            var numPerAlloc = maxAllocSize / stubSize;
            var mainAllocSize = numPerAlloc * stubSize;
            var lastAllocSize = allStubsSize % mainAllocSize;
            Helpers.DAssert(mainAllocSize > lastAllocSize);

            var allocs = new IAllocatedMemory[numMainAllocs + (lastAllocSize != 0 ? 1 : 0)];

            var mainAllocArr = ArrayPool<byte>.Shared.Rent(mainAllocSize);
            var offsetArr = ArrayPool<nint>.Shared.Rent(numPerAlloc);
            var mainAllocBuf = mainAllocArr.AsSpan().Slice(0, mainAllocSize);

            // we want to fill the buffer once, then for each alloc, only set the indicies
            for (var i = 0; i < numPerAlloc; i++)
            {
                stubData.CopyTo(mainAllocBuf.Slice(i * stubSize));
            }


            ref var vtblBase = ref Unsafe.AsRef<IntPtr>((void*)vtableBase);

            // now we want to start making our allocations and filling the input vtable pointer
            // we will be using the same alloc request for all of them
            var allocReq = new AllocationRequest(mainAllocSize)
            {
                // the allocations themselves also need to be aligned according to CFG's requirements
                Alignment = cfg is not null ? cfg.TargetAlignmentRequirement : IntPtr.Size,
                Executable = true
            };

            for (var i = 0; i < numMainAllocs; i++)
            {
                Helpers.Assert(system.MemoryAllocator.TryAllocate(allocReq, out var alloc));
                allocs[i] = alloc;

                // fill the indicies appropriately
                FillBufferIndicies(stubSize, indexOffs, numPerAlloc, i, mainAllocBuf, premulOffset);
                FillVtbl(stubSize, numPerAlloc * i, ref vtblBase, numPerAlloc, alloc.BaseAddress, offsetArr);

                // patch the alloc to contain our data
                system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, mainAllocBuf, default);

                // register the entries with CFG
                cfg?.RegisterValidIndirectCallTargets((void*)alloc.BaseAddress, alloc.Size, offsetArr.AsSpan(0, numPerAlloc));
            }

            // now, if we need one final alloc, do that
            if (lastAllocSize > 0)
            {
                allocReq = allocReq with { Size = lastAllocSize };

                Helpers.Assert(system.MemoryAllocator.TryAllocate(allocReq, out var alloc));
                allocs[allocs.Length - 1] = alloc;

                var numEntries = lastAllocSize / stubSize;

                // fill the indicies appropriately
                FillBufferIndicies(stubSize, indexOffs, numPerAlloc, numMainAllocs, mainAllocBuf, premulOffset);
                FillVtbl(stubSize, numPerAlloc * numMainAllocs, ref vtblBase, numEntries, alloc.BaseAddress, offsetArr);

                // patch the alloc to contain our data
                system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, mainAllocBuf.Slice(0, lastAllocSize), default);

                // register the entries with CFG
                cfg?.RegisterValidIndirectCallTargets((void*)alloc.BaseAddress, alloc.Size, offsetArr.AsSpan(0, numEntries));
            }

            ArrayPool<nint>.Shared.Return(offsetArr);
            ArrayPool<byte>.Shared.Return(mainAllocArr);

            return allocs;

            static void FillBufferIndicies(int stubSize, int indexOffs, int numPerAlloc, int i, Span<byte> mainAllocBuf, bool premul)
            {
                for (var j = 0; j < numPerAlloc; j++)
                {
                    ref var indexBase = ref mainAllocBuf[(j * stubSize) + indexOffs];
                    var index = (uint)((numPerAlloc * i) + j);
                    if (premul)
                    {
                        index *= (uint)IntPtr.Size;
                    }
                    Unsafe.WriteUnaligned(ref indexBase, index);
                }
            }

            static void FillVtbl(int stubSize, int baseIndex, ref IntPtr vtblBase, int numEntries, nint baseAddr, nint[] offsets)
            {
                for (var i = 0; i < numEntries; i++)
                {
                    var offs = offsets[i] = stubSize * i;
                    Unsafe.Add(ref vtblBase, baseIndex + i) = baseAddr + offs;
                }
            }
        }
    }
}
