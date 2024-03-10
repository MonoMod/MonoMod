using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms.Memory
{
    /// <summary>
    /// A base type providing OS methods required for <see cref="QueryingPagedMemoryAllocator"/>.
    /// </summary>
    public abstract class QueryingMemoryPageAllocatorBase
    {
        /// <summary>
        /// Gets the page size.
        /// </summary>
        public abstract uint PageSize { get; }
        /// <summary>
        /// Tries to query the specified page for information.
        /// </summary>
        /// <param name="pageAddr">The address of the page to query.</param>
        /// <param name="isFree"><see langword="true"/> if the page is free; <see langword="false"/> if it is allocated.</param>
        /// <param name="allocBase">The base of the page allocation this page is a part of.</param>
        /// <param name="allocSize">The size of the page allocation this page is a part of.</param>
        /// <returns><see langword="true"/> if the page was successfully queried; <see langword="false"/> otherwise.</returns>
        public abstract bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize);
        /// <summary>
        /// Tries to allocate a page.
        /// </summary>
        /// <param name="size">The size of the page allocation.</param>
        /// <param name="executable"><see langword="true"/> if the page should be executable; <see langword="false"/> otherwise.</param>
        /// <param name="allocated">The address of the allocated page, if successful.</param>
        /// <returns><see langword="true"/> if a page was successfully allocated; <see langword="false"/> otherwise.</returns>
        public abstract bool TryAllocatePage(nint size, bool executable, out IntPtr allocated);
        /// <summary>
        /// Tries to allocate a specific page.
        /// </summary>
        /// <param name="pageAddr">The address of the page to allocate.</param>
        /// <param name="size">The size of the page allocation.</param>
        /// <param name="executable"><see langword="true"/> if the page should be executable; <see langword="false"/> otherwise.</param>
        /// <param name="allocated">The address of the allocated page, if successful.</param>
        /// <returns><see langword="true"/> if a page was successfully allocated; <see langword="false"/> otherwise.</returns>
        public abstract bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated);
        /// <summary>
        /// Tries to free the page at the provided addresss.
        /// </summary>
        /// <param name="pageAddr">The address of the page to free.</param>
        /// <param name="errorMsg">An error message describing the error that ocurred, if any.</param>
        /// <returns><see langword="true"/> if the page was successfully freed; <see langword="false"/> otherwise.</returns>
        public abstract bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg);
    }

    /// <summary>
    /// A <see cref="PagedMemoryAllocator"/> built around querying pages in memory.
    /// </summary>
    public sealed class QueryingPagedMemoryAllocator : PagedMemoryAllocator
    {
        private readonly QueryingMemoryPageAllocatorBase pageAlloc;
        /// <summary>
        /// Constructs a <see cref="QueryingPagedMemoryAllocator"/> using the provided <see cref="QueryingMemoryPageAllocatorBase"/>.
        /// </summary>
        /// <param name="alloc">The page allocator to use.</param>
        public QueryingPagedMemoryAllocator(QueryingMemoryPageAllocatorBase alloc)
            : base((nint)Helpers.ThrowIfNull(alloc).PageSize)
        {
            pageAlloc = alloc;
        }

        /// <inheritdoc/>
        protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
        {
            if (!pageAlloc.TryAllocatePage(PageSize, request.Executable, out var allocBase))
            {
                allocated = null;
                return false;
            }

            var pageObj = new Page(this, allocBase, (uint)PageSize, request.Executable);
            InsertAllocatedPage(pageObj);

            // now that we have a page, we'll try to allocate out of it
            // if that fails, immediately register for cleanup
            if (!pageObj.TryAllocate((uint)request.Size, (uint)request.Alignment, out var alloc))
            {
                RegisterForCleanup(pageObj);
                allocated = null;
                return false;
            }

            // we successfully allocated, return the page allocation
            allocated = alloc;
            return true;
        }

        /// <inheritdoc/>
        protected override bool TryAllocateNewPage(PositionedAllocationRequest request, nint targetPage, nint lowPageBound, nint highPageBound, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
        {
            // we'll do the same approach for trying to find an existing page, but querying the OS for free pages to allocate
            var target = request.Target;

            var lowPage = targetPage;
            var highPage = targetPage + PageSize;

            while (lowPage >= lowPageBound || highPage < highPageBound)
            {
                // first check the high pages, while they're closer than low pages
                while (
                    highPage < highPageBound &&
                    (lowPage < lowPageBound || target - lowPage > highPage - target)
                )
                {
                    if (TryAllocNewPage(request, ref highPage, true, out allocated))
                        return true;
                }

                // then try low pages, while they're closer than high pages
                while (
                    lowPage >= lowPageBound &&
                    (highPage >= highPageBound || target - lowPage < highPage - target)
                )
                {
                    if (TryAllocNewPage(request, ref lowPage, false, out allocated))
                        return true;
                }
            }

            // if we fall out to here, we just couldn't allocate, so sucks
            allocated = null;
            return false;
        }

        private unsafe bool TryAllocNewPage(PositionedAllocationRequest request, ref nint page, bool goingUp, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
        {
            if (pageAlloc.TryQueryPage(page, out var isFree, out var baseAddr, out var allocSize))
            {
                if (!isFree) // this is not a free block, so we don't care
                    goto Fail;

                if (!pageAlloc.TryAllocatePage(page, PageSize, request.Base.Executable, out var allocBase)) // allocation failed
                    goto Fail;

                var pageObj = new Page(this, allocBase, (uint)PageSize, request.Base.Executable);
                InsertAllocatedPage(pageObj);

                // now that we have a page, we'll try to allocate out of it
                // if that fails, immediately register for cleanup
                if (!pageObj.TryAllocate((uint)request.Base.Size, (uint)request.Base.Alignment, out var alloc))
                {
                    RegisterForCleanup(pageObj);
                    goto Fail;
                }

                // we successfully allocated, return the page allocation
                allocated = alloc;
                return true;

                Fail:
                // We're failing out, update the page address appropriately
                if (goingUp)
                    page = baseAddr + allocSize;
                else
                    page = baseAddr - PageSize;

                allocated = null;
                return false;
            }
            else
            {
                // TODO: check GetLastError

                // query failed, fail out
                if (goingUp)
                    page += PageSize;
                else
                    page -= PageSize;
                allocated = null;
                return false;
            }
        }

        /// <inheritdoc/>
        protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg)
            => pageAlloc.TryFreePage(page.BaseAddr, out errorMsg);
    }
}
