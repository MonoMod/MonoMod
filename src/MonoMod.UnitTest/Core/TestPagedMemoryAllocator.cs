using MonoMod.Core.Platforms;
using MonoMod.Core.Platforms.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    public sealed class TestPagedMemoryAllocator : TestBase
    {
        public TestPagedMemoryAllocator(ITestOutputHelper helper) : base(helper)
        {
        }

        private sealed class DummyMemoryAllocator : PagedMemoryAllocator
        {
            public DummyMemoryAllocator() : base(0x1000)
            {
            }

            private nint addr = 0x10000;
            public int PositionedPageAllocations { get; private set; }

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
            {
                // each alloc will create a new "page", because we only *really* care about testing the page-list for now
                var page = new Page(this, (nint)addr++, (uint)PageSize, request.Executable);
                InsertAllocatedPage(page);
                var result = page.TryAllocate((uint)request.Size, (uint)request.Alignment, out var pageAlloc);
                allocated = pageAlloc;
                return result;
            }

            protected override bool TryAllocateNewPage(PositionedAllocationRequest request, nint targetPage, nint lowPageBound, nint highPageBound, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
            {
                // each alloc will create a new "page", because we only *really* care about testing the page-list for now
                PositionedPageAllocations++;
                var page = new Page(this, (nint)targetPage, (uint)PageSize, request.Base.Executable);
                InsertAllocatedPage(page);
                var result = page.TryAllocate((uint)request.Base.Size, (uint)request.Base.Alignment, out var pageAlloc);
                allocated = pageAlloc;
                return result;
            }

            protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg)
            {
                // no-op
                errorMsg = null;
                return true;
            }
        }


        [Fact]
        public void PagedMemoryAllocatorCanHandleLotsOfPages()
        {
            var memAllocator = new DummyMemoryAllocator();

            var list = new List<IAllocatedMemory>();
            // allocate 64 pages
            for (var i = 0; i < 64; i++)
            {
                Assert.True(memAllocator.TryAllocate(new(memAllocator.MaxSize), out var allocated));
                list.Add(allocated);
            }

            var ct = 0;
            do
            {
                // destroy half of them
                for (var i = 0; i < list.Count / 2; i++)
                {
                    list[i].Dispose();
                }

                // wait for a GC
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // allocate that front half again
                for (var i = 0; i < list.Count / 2; i++)
                {
                    Assert.True(memAllocator.TryAllocate(new(memAllocator.MaxSize), out var allocated));
                    list[i] = allocated;
                }
            }
            while (ct++ < 16); // repeat

            foreach (var it in list)
            {
                it.Dispose();
            }
        }

        [Fact]
        public void TryAllocateInRangeReusesFirstPage()
        {
            var allocator = new DummyMemoryAllocator();
            Assert.True(allocator.TryAllocate(new(16), out var source));
            using (source)
            {
                nint target = source.BaseAddress;
                var request = new PositionedAllocationRequest(target, target - 0x1000, target + 0x1000, new(16));
                Assert.True(allocator.TryAllocateInRange(request, out var near));
                using (near)
                {
                    Assert.Equal(target + 16, (nint)near.BaseAddress);
                }

                Assert.Equal(0, allocator.PositionedPageAllocations);
            }
        }

        private sealed class ReusingMemoryAllocator : PagedMemoryAllocator
        {
            public ReusingMemoryAllocator() : base(0x1000)
            {
            }

            public int FreedPages;
            public Action? OnFree;

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
            {
                // every new page gets the same address, like the OS reusing the address of a freed page
                var page = new Page(this, (nint)0x10000, (uint)PageSize, request.Executable);
                InsertAllocatedPage(page);
                var result = page.TryAllocate((uint)request.Size, (uint)request.Alignment, out var pageAlloc);
                allocated = pageAlloc;
                return result;
            }

            protected override bool TryAllocateNewPage(PositionedAllocationRequest request, nint targetPage, nint lowPageBound, nint highPageBound, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
                => TryAllocateNewPage(request.Base, out allocated);

            protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg)
            {
                FreedPages++;
                var onFree = OnFree;
                OnFree = null;
                onFree?.Invoke();
                errorMsg = null;
                return true;
            }
        }

        [Fact]
        public void FreedPageAddressCanBeReused()
        {
            var memAllocator = new ReusingMemoryAllocator();

            // emptying the same page twice queues it for cleanup twice
            Assert.True(memAllocator.TryAllocate(new(16), out var first));
            first.Dispose();
            Assert.True(memAllocator.TryAllocate(new(16), out var second));
            second.Dispose();

            // someone else gets a new page at the same address as soon as the old one is freed
            IAllocatedMemory? reused = null;
            memAllocator.OnFree = () => memAllocator.TryAllocate(new(16), out reused);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.NotNull(reused);
            Assert.Equal(1, memAllocator.FreedPages);
            reused.Dispose();
        }
    }
}
