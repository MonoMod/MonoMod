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

            private nint addr;

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
    }
}
