using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BitOperations = System.Numerics.BitOperationsEx;

namespace MonoMod.Core.Platforms.Memory
{
    /// <summary>
    /// An <see cref="IMemoryAllocator"/> based on a paged memory model.
    /// </summary>
    public abstract class PagedMemoryAllocator : IMemoryAllocator
    {
        private sealed class FreeMem
        {
            public uint BaseOffset;
            public uint Size;
            public FreeMem? NextFree;
        }

        /// <summary>
        /// An allocation of memory within a page.
        /// </summary>
        protected sealed class PageAllocation : IAllocatedMemory
        {

            private readonly Page owner;
            private readonly uint offset;

            /// <inheritdoc/>
            public bool IsExecutable => owner.IsExecutable;

            /// <summary>
            /// Creates a new <see cref="PageAllocation"/> in the provided page, at the specified offset, with the specified size.
            /// </summary>
            /// <param name="page">The <see cref="Page"/> this allocation is a part of.</param>
            /// <param name="offset">The offset in the page of this allocation.</param>
            /// <param name="size">The size of the allocation.</param>
            public PageAllocation(Page page, uint offset, int size)
            {
                owner = page;
                this.offset = offset;
                Size = size;
            }

            /// <inheritdoc/>
            public IntPtr BaseAddress => owner.BaseAddr + (nint)offset;

            /// <inheritdoc/>
            public int Size { get; }

            /// <inheritdoc/>
            public unsafe Span<byte> Memory => new((void*)BaseAddress, Size);

            #region IDisposable implementation
            private bool disposedValue;

            private void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    owner.FreeMem(offset, (uint)Size);
                    disposedValue = true;
                }
            }

            /// <summary>
            /// Releases the allocated memory represented by this allocation.
            /// </summary>
            ~PageAllocation()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: false);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }

        /// <summary>
        /// A page of memory managed by a <see cref="PagedMemoryAllocator"/>.
        /// </summary>
        protected sealed class Page
        {

            private readonly PagedMemoryAllocator owner;

            private readonly object sync = new();

            // we keep freeList sorted by BaseOffset, possibly with nulls between
            private FreeMem? freeList;

            /// <summary>
            /// Gets whether or not this page has no allocations.
            /// </summary>
            public bool IsEmpty => freeList is { } list && list.BaseOffset == 0 && list.Size == Size;
            /// <summary>
            /// Gets the base address of this page.
            /// </summary>
            public IntPtr BaseAddr { get; }
            /// <summary>
            /// Gets the size of this page.
            /// </summary>
            public uint Size { get; }
            /// <summary>
            /// Gets whether or not this page is executable.
            /// </summary>
            public bool IsExecutable { get; }

            /// <summary>
            /// Constructs a <see cref="Page"/> object associated with the specified <see cref="PagedMemoryAllocator"/>.
            /// </summary>
            /// <param name="owner">The <see cref="PagedMemoryAllocator"/> which this page is managed by.</param>
            /// <param name="baseAddr">The base address of this page.</param>
            /// <param name="size">The size of this page.</param>
            /// <param name="isExecutable"><see langword="true"/> if this page is executable; <see langword="false"/> otherwise.</param>
            public Page(PagedMemoryAllocator owner, IntPtr baseAddr, uint size, bool isExecutable)
            {
                this.owner = owner;
                (BaseAddr, Size) = (baseAddr, size);
                IsExecutable = isExecutable;
                freeList = new FreeMem
                {
                    BaseOffset = 0,
                    Size = size,
                    NextFree = null,
                };
            }

            /// <summary>
            /// Tries to create a new allocation in this page.
            /// </summary>
            /// <param name="size">The size of the allocation.</param>
            /// <param name="align">The alignment of the allocation.</param>
            /// <param name="alloc">THe new allocation, if one was made.</param>
            /// <returns><see langword="true"/> if an allocation was made; <see langword="false"/> otherwise.</returns>
            public bool TryAllocate(uint size, uint align, [MaybeNullWhen(false)] out PageAllocation alloc)
            {
                // the sizes we allocate we want to round to a power of two
                //size = BitOperations.RoundUpToPowerOf2(size);
                lock (sync)
                {

                    ref var ptrNode = ref freeList;

                    uint alignOffset = 0;
                    while (ptrNode is not null)
                    {
                        var alignFix = ptrNode.BaseOffset % align;
                        alignFix = alignFix != 0 ? align - alignFix : alignFix;
                        if (ptrNode.Size >= alignFix + size)
                        {
                            alignOffset = alignFix;
                            break; // we found our node
                        }

                        // otherwise, move to the next one
                        ptrNode = ref ptrNode.NextFree;
                    }

                    if (ptrNode is null)
                    {
                        // we couldn't find a free node large enough
                        alloc = null;
                        return false;
                    }

                    var offs = ptrNode.BaseOffset + alignOffset;

                    if (alignOffset == 0)
                    {
                        // if the align offset is zero, then we just allocate out of the front of this node
                        ptrNode.BaseOffset += size;
                        ptrNode.Size -= size;

                        // removing zero-size enties is done in normalize
                    }
                    else
                    {
                        // otherwise, we have to split the free node

                        // create the front half
                        var frontNode = new FreeMem
                        {
                            BaseOffset = ptrNode.BaseOffset,
                            Size = alignOffset,
                            NextFree = ptrNode,
                        };

                        // push back the back half
                        ptrNode.BaseOffset += alignOffset + size;
                        ptrNode.Size -= alignOffset + size;

                        // removing zero-size enties is done in normalize

                        // update our pointer to point at the new node
                        ptrNode = frontNode;
                    }

                    // now we normalize the free list, to ensure its in a sane state
                    NormalizeFreeList();

                    // and can now actually create the allocation object
                    alloc = new PageAllocation(this, offs, (int)size);

                    return true;
                }
            }

            private void NormalizeFreeList()
            {
                ref var node = ref freeList;

                while (node is not null)
                {
                    if (node.Size <= 0)
                    {
                        // if the node size is zero, remove it from the list
                        node = node.NextFree;
                        continue; // and retry with new value
                    }

                    if (node.NextFree is { } next &&
                        next.BaseOffset == node.BaseOffset + node.Size)
                    {
                        // if the next node exists and starts at our end, combine down and remove next
                        node.Size += next.Size;
                        node.NextFree = next.NextFree;

                        // now we want to loop back to the top *without* advancing down the chain to continue this
                        continue;
                    }

                    node = ref node.NextFree;
                }
            }

            // correctness relies on this only being called internally by PageAlloc's Dispose()
            internal void FreeMem(uint offset, uint size)
            {
                lock (sync)
                {
                    ref var node = ref freeList;

                    while (node is not null)
                    {
                        if (node.BaseOffset > offset) // we found the first node with greater offset, break out
                            break;
                        node = ref node.NextFree;
                    }

                    // now node points to where we need to store the new FreeMem, as well as its next node
                    node = new FreeMem
                    {
                        BaseOffset = offset,
                        Size = size,
                        NextFree = node,
                    };
                    NormalizeFreeList();

                    if (IsEmpty)
                        owner.RegisterForCleanup(this);
                }
            }
        }

        private readonly nint pageBaseMask;
        private readonly nint pageSize;
        private readonly bool pageSizeIsPow2;

        /// <summary>
        /// Gets the page size.
        /// </summary>
        protected nint PageSize => pageSize;

        /// <summary>
        /// Constructs a <see cref="PagedMemoryAllocator"/> with the specified page size.
        /// </summary>
        /// <param name="pageSize"></param>
        protected PagedMemoryAllocator(nint pageSize)
        {
            this.pageSize = pageSize;

            pageSizeIsPow2 = BitOperations.IsPow2(pageSize);
            pageBaseMask = ~(nint)0 << BitOperations.TrailingZeroCount(pageSize);
        }

        /// <summary>
        /// Rounds <paramref name="addr"/> down to the nearest page boundary below it.
        /// </summary>
        /// <param name="addr">The address to round.</param>
        /// <returns>The dounded address.</returns>
        public nint RoundDownToPageBoundary(nint addr)
        {
            if (pageSizeIsPow2)
                return addr & pageBaseMask;
            else
                return addr - addr % pageSize;
        }

        private Page?[] allocationList = new Page?[16];
        private int pageCount;

        private readonly struct PageComparer : IComparer<Page?>
        {
            public int Compare(Page? x, Page? y)
            {
                if (x == y)
                    return 0;
                if (x is null)
                    return 1;
                if (y is null)
                    return -1;

                return ((long)x.BaseAddr).CompareTo((long)y.BaseAddr);
            }
        }

        private readonly struct PageAddrComparable : IComparable<Page>
        {
            private readonly IntPtr addr;
            public PageAddrComparable(IntPtr addr) => this.addr = addr;
            public int CompareTo(Page? other)
            {
                if (other is null)
                    return 1;

                return ((long)addr).CompareTo((long)other.BaseAddr);
            }
        }

        /// <summary>
        /// Inserts a newly allocated <see cref="Page"/> into this allocator.
        /// </summary>
        /// <remarks>
        /// The allocation lock must be held when this is called.
        /// </remarks>
        /// <param name="page">The page to insert.</param>
        protected void InsertAllocatedPage(Page page)
        {
            if (pageCount == allocationList.Length)
            {
                // we need to expand the allocationList
                var newSize = (int)BitOperations.RoundUpToPowerOf2((uint)allocationList.Length + 1);
                Array.Resize(ref allocationList, newSize);
            }

            var list = allocationList.AsSpan();

            var insertAt = list.Slice(0, pageCount).BinarySearch(page, new PageComparer());

            if (insertAt >= 0)
            {
                // the page is already in the list, no work needed
                return;
            }
            else
            {
                insertAt = ~insertAt;
            }

            // insertAt is the index of the next item larger, which is the index we want the page to be at
            if (insertAt + 1 < list.Length)
            {
                list.Slice(insertAt, pageCount - insertAt).CopyTo(list.Slice(insertAt + 1));
            }
            list[insertAt] = page;
            pageCount++;
        }

        private void RemoveAllocatedPage(Page page)
        {
            var list = allocationList.AsSpan();

            var indexToRemove = list.Slice(0, pageCount).BinarySearch(page, new PageComparer());

            if (indexToRemove < 0)
            {
                // the page doesn't exist, nothing needs to be done
                return;
            }

            // just copy from above the index down
            list.Slice(indexToRemove + 1).CopyTo(list.Slice(indexToRemove));
            pageCount--;
        }

        private ReadOnlySpan<Page> AllocList => allocationList.AsSpan().Slice(0, pageCount)!;

        private int GetBoundIndex(IntPtr ptr)
        {
            var index = AllocList.BinarySearch(new PageAddrComparable(ptr));

            return index >= 0 ? index : ~index;
        }

        private readonly ConcurrentBag<Page> pagesToClean = new();
        private int registeredForCleanup;

        /// <summary>
        /// Registers the provided page for cleanup.
        /// </summary>
        /// <remarks>
        /// Pages are not freed when their last allocation is. Instead, we wait for the next GC to free them, to allow
        /// other allocation requests to reuse that page without calling into the OS.
        /// </remarks>
        /// <param name="page">The page to register for cleanup.</param>
        protected void RegisterForCleanup(Page page)
        {
            if (Environment.HasShutdownStarted || AppDomain.CurrentDomain.IsFinalizingForUnload())
                return;

            pagesToClean.Add(page);

            if (Interlocked.CompareExchange(ref registeredForCleanup, 1, 0) == 0)
                Gen2GcCallback.Register(DoCleanup);
        }

        private bool DoCleanup()
        {
            if (Environment.HasShutdownStarted || AppDomain.CurrentDomain.IsFinalizingForUnload())
                return false;

            Volatile.Write(ref registeredForCleanup, 0);

            while (pagesToClean.TryTake(out var page))
            {
                lock (sync)
                {
                    // if the page is no longer empty, don't free
                    if (!page.IsEmpty)
                        continue;

                    // otherwise, remove it from the allocation list, so we can free it outside the lock
                    RemoveAllocatedPage(page);
                }

                // now we can actually free the associated memory
                if (!TryFreePage(page, out var error))
                {
                    // free failed; log the error and move on
                    MMDbgLog.Error($"Could not deallocate page! {error}");
                }
            }

            // this should never be re-registered for later GCs, at least until another page is marked as needing cleaning
            return false;
        }

        /// <summary>
        /// Tries to free a page.
        /// </summary>
        /// <param name="page">The page to free.</param>
        /// <param name="errorMsg">The error message generated by the operation, if any./</param>
        /// <returns><see langword="true"/> if the page was successfully freed; <see langword="false"/> otherwise.</returns>
        protected abstract bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg);

        private readonly object sync = new();

        /// <inheritdoc/>
        public int MaxSize => (int)pageSize;

        /// <inheritdoc/>
        public bool TryAllocateInRange(PositionedAllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
        {
            if ((nint)request.Target < request.LowBound || (nint)request.Target > request.HighBound)
                throw new ArgumentException("Target not between low and high", nameof(request));

            if (request.Base.Size < 0)
                throw new ArgumentException("Size is negative", nameof(request));
            if (request.Base.Alignment <= 0)
                throw new ArgumentException("Alignment is zero or negative", nameof(request));

            if (request.Base.Size > pageSize)                 // TODO: large allocations
                throw new NotSupportedException("Single allocations cannot be larger than a page");

            // we want to round the low value up
            var lowPageBound = RoundDownToPageBoundary(request.LowBound + pageSize - 1);
            // and the high value down
            var highPageBound = RoundDownToPageBoundary(request.HighBound);

            var targetPage = RoundDownToPageBoundary(request.Target);

            var target = (nint)request.Target;

            lock (sync)
            {
                var lowIdxBound = GetBoundIndex(lowPageBound);
                var highIdxBound = GetBoundIndex(highPageBound);

                if (lowIdxBound != highIdxBound)
                {
                    // there are pages for us to check within the target range
                    // lets see where our target lands us
                    var targetIndex = GetBoundIndex(targetPage);

                    // we'll check pages starting at targetIndex and expanding around it

                    var lowIdx = targetIndex - 1;
                    var highIdx = targetIndex;

                    while ((uint)highIdx <= AllocList.Length && (uint)lowIdx < AllocList.Length
                        && (lowIdx >= lowIdxBound || highIdx < highIdxBound))
                    {
                        // try high pages, while they're closer than low pages
                        while (
                            (uint)highIdx < AllocList.Length &&
                            highIdx < highIdxBound &&
                            (lowIdx < lowIdxBound || target - AllocList[lowIdx].BaseAddr > AllocList[highIdx].BaseAddr - target)
                        )
                        {
                            if (TryAllocWithPage(AllocList[highIdx], request, out allocated))
                                return true;
                            highIdx++;
                        }

                        // then try low pages, while they're closer than high pages
                        while (
                            (uint)lowIdx < AllocList.Length &&
                            lowIdx >= lowIdxBound &&
                            (highIdx >= highIdxBound || target - AllocList[lowIdx].BaseAddr < AllocList[highIdx].BaseAddr - target)
                        )
                        {
                            if (TryAllocWithPage(AllocList[lowIdx], request, out allocated))
                                return true;
                            lowIdx++;
                        }
                    }

                    // if we fall out here, no adequate page was found
                }

                // if we make it here, we need to allocate a page from the OS

                return TryAllocateNewPage(request, targetPage, lowPageBound, highPageBound, out allocated);
            }
        }

        private static bool TryAllocWithPage(Page page, PositionedAllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
        {
            if (page.IsExecutable == request.Base.Executable && page.BaseAddr >= (nint)request.LowBound && page.BaseAddr < (nint)request.HighBound)
            {
                if (page.TryAllocate((uint)request.Base.Size, (uint)request.Base.Alignment, out var pageAlloc))
                {
                    if ((nint)pageAlloc.BaseAddress >= request.LowBound && (nint)pageAlloc.BaseAddress < request.HighBound)
                    {
                        // we've found a valid allocation, we're done
                        allocated = pageAlloc;
                        return true;
                    }
                    else
                    {
                        // this allocation isn't within the bounds (not sure how this could happen, but check anyway)
                        // we deallocate by disposing, and move on
                        pageAlloc.Dispose();
                    }
                }
            }

            allocated = null;
            return false;
        }

        /// <inheritdoc/>
        public bool TryAllocate(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
        {
            if (request.Size < 0)
                throw new ArgumentException("Size is negative", nameof(request));
            if (request.Alignment <= 0)
                throw new ArgumentException("Alignment is zero or negative", nameof(request));

            if (request.Size > pageSize) // TODO: large allocations
                throw new NotSupportedException("Single allocations cannot be larger than a page");

            lock (sync)
            {
                foreach (var page in AllocList)
                {
                    if (page.IsExecutable == request.Executable && page.TryAllocate((uint)request.Size, (uint)request.Alignment, out var alloc))
                    {
                        allocated = alloc;
                        return true;
                    }
                }

                return TryAllocateNewPage(request, out allocated);
            }
        }

        /// <summary>
        /// Tries to allocate memory from a newly allocated page, according to the provided <see cref="AllocationRequest"/>.
        /// </summary>
        /// <param name="request">The allocation request.</param>
        /// <param name="allocated">The allocated memory, if any.</param>
        /// <returns><see langword="true"/> if memory was successfully allocated; <see langword="false"/> otherwise.</returns>
        protected abstract bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
        /// <summary>
        /// Tries to allocate memory from a newly allocated page, according to the provided <see cref="PositionedAllocationRequest"/>.
        /// </summary>
        /// <param name="request">The allocation request.</param>
        /// <param name="targetPage">The target page address.</param>
        /// <param name="lowPageBound">The low page boundary.</param>
        /// <param name="highPageBound">The high page boundary.</param>
        /// <param name="allocated">The allocated memory, if any.</param>
        /// <returns><see langword="true"/> if memory was successfully allocated; <see langword="false"/> otherwise.</returns>
        protected abstract bool TryAllocateNewPage(PositionedAllocationRequest request, nint targetPage, nint lowPageBound, nint highPageBound, [MaybeNullWhen(false)] out IAllocatedMemory allocated);

    }
}
