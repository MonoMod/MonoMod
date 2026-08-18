using MonoMod.Core.Platforms;
using System;
using Xunit;

namespace MonoMod.UnitTest.Github
{
    [CollectionDefinition(nameof(Issue308), DisableParallelization = true)]
    [Collection(nameof(Issue308))]
    public class Issue308
    {
        [Fact]
        public void TryAllocateInRangeDoesNotReturnOutsideBoundsForZero()
        {
            var alloc = PlatformTriple.Current.System.MemoryAllocator;
            nint pageSize = alloc.MaxSize;

            nint target = 0;
            nint low = 0;
            var high = pageSize;

            var req = new PositionedAllocationRequest(target, low, high, new AllocationRequest(IntPtr.Size) { Executable = true });
            if (alloc.TryAllocateInRange(req, out var allocated))
            {
                var addr = (nint)allocated!.BaseAddress;
                allocated.Dispose();
                Assert.True(addr >= low && addr < high, $"allocator returned 0x{addr:X}, outside requested bounds [0x{low:X}, 0x{high:X})");
            }
        }
    }
}
