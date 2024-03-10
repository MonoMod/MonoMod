using MonoMod.Utils;
using System;
using System.Buffers;

namespace MonoMod.Core.Platforms.Architectures
{
    internal static class Shared
    {
        public static unsafe ReadOnlyMemory<IAllocatedMemory> CreateVtableStubs(ISystem system, IntPtr vtableBase, int vtableSize, ReadOnlySpan<byte> stubData, int indexOffs, bool premulOffset)
        {
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
            for (var i = 0; i < numPerAlloc; i++)
            {
                stubData.CopyTo(mainAllocBuf.Slice(i * stubData.Length));
            }

            ref var vtblBase = ref Unsafe.AsRef<IntPtr>((void*)vtableBase);

            // now we want to start making our allocations and filling the input vtable pointer
            // we will be using the same alloc request for all of them
            var allocReq = new AllocationRequest(mainAllocSize)
            {
                Alignment = IntPtr.Size,
                Executable = true
            };

            for (var i = 0; i < numMainAllocs; i++)
            {
                Helpers.Assert(system.MemoryAllocator.TryAllocate(allocReq, out var alloc));
                allocs[i] = alloc;

                // fill the indicies appropriately
                FillBufferIndicies(stubData.Length, indexOffs, numPerAlloc, i, mainAllocBuf, premulOffset);
                FillVtbl(stubData.Length, numPerAlloc * i, ref vtblBase, numPerAlloc, alloc.BaseAddress);

                // patch the alloc to contain our data
                system.PatchData(PatchTargetKind.Executable, alloc.BaseAddress, mainAllocBuf, default);
            }

            // now, if we need one final alloc, do that
            if (lastAllocSize > 0)
            {
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

            static void FillBufferIndicies(int stubSize, int indexOffs, int numPerAlloc, int i, Span<byte> mainAllocBuf, bool premul)
            {
                for (var j = 0; j < numPerAlloc; j++)
                {
                    ref var indexBase = ref mainAllocBuf[j * stubSize + indexOffs];
                    var index = (uint)(numPerAlloc * i + j);
                    if (premul)
                    {
                        index *= (uint)IntPtr.Size;
                    }
                    Unsafe.WriteUnaligned(ref indexBase, index);
                }
            }

            static void FillVtbl(int stubSize, int baseIndex, ref IntPtr vtblBase, int numEntries, nint baseAddr)
            {
                for (var i = 0; i < numEntries; i++)
                {
                    Unsafe.Add(ref vtblBase, baseIndex + i) = baseAddr + stubSize * i;
                }
            }
        }
    }
}
