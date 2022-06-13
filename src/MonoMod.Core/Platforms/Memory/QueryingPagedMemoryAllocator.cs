using System;
using System.Diagnostics.CodeAnalysis;
using MonoMod.Core.Utils;

namespace MonoMod.Core.Platforms.Memory {
    public abstract class QueryingMemoryPageAllocatorBase {
        public abstract uint PageSize { get; }
        public abstract bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize);
        public abstract bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated);
        public abstract bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg);
    }

    public sealed class QueryingPagedMemoryAllocator : PagedMemoryAllocator {
        private readonly QueryingMemoryPageAllocatorBase pageAlloc;
        public QueryingPagedMemoryAllocator(QueryingMemoryPageAllocatorBase alloc)
            : base((nint) Helpers.ThrowIfNull(alloc).PageSize) {
            pageAlloc = alloc;
        }

        protected override bool TryAllocateNewPage(AllocationRequest request, nint targetPage, nint lowPageBound, nint highPageBound, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
            // we'll do the same approach for trying to find an existing page, but querying the OS for free pages to allocate
            var target = request.Target;

            var lowPage = targetPage;
            var highPage = targetPage + PageSize;

            while (lowPage >= lowPageBound || highPage < highPageBound) {
                // first check the high pages, while they're closer than low pages
                while (
                    highPage < highPageBound &&
                    (lowPage < lowPageBound || target - lowPage > highPage - target)
                )                     if (TryAllocNewPage(request, ref highPage, true, out allocated))
                        return true;

                // then try low pages, while they're closer than high pages
                while (
                    lowPage >= lowPageBound &&
                    (highPage >= highPageBound || target - lowPage < highPage - target)
                )                     if (TryAllocNewPage(request, ref lowPage, false, out allocated))
                        return true;
            }

            // if we fall out to here, we just couldn't allocate, so sucks
            allocated = null;
            return false;
        }

        private unsafe bool TryAllocNewPage(AllocationRequest request, ref nint page, bool goingUp, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
            if (pageAlloc.TryQueryPage(page, out var isFree, out IntPtr baseAddr, out var allocSize)) {
                if (!isFree)                     // this is not a free block, so we don't care
                    goto Fail;

                if (!pageAlloc.TryAllocatePage(page, PageSize, request.Executable, out var allocBase))                     // allocation failed
                    goto Fail;

                var pageObj = new Page(this, allocBase, (uint) PageSize, request.Executable);
                InsertAllocatedPage(pageObj);

                // now that we have a page, we'll try to allocate out of it
                // if that fails, immediately register for cleanup
                if (!pageObj.TryAllocate((uint) request.Size, (uint) request.Alignment, out var alloc)) {
                    RegisterForCleanup(pageObj);
                    goto Fail;
                }

                // we successfully allocated, return the page allocation
                allocated = alloc;
                return true;

                Fail:
                // We're failing out, update the page address appropriately
                if (goingUp)                     page = baseAddr + allocSize;
else                     page = baseAddr - PageSize;

                allocated = null;
                return false;
            } else {
                // TODO: check GetLastError

                // query failed, fail out
                if (goingUp)                     page += PageSize;
else                     page -= PageSize;
                allocated = null;
                return false;
            }
        }

        protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg)
            => pageAlloc.TryFreePage(page.BaseAddr, out errorMsg);
    }
}
