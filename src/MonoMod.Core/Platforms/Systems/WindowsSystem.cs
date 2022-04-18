using MonoMod.Backports;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.Core.Platforms.Systems {
    internal class WindowsSystem : ISystem {
        public OSKind Target => OSKind.Windows;

        public SystemFeature Features => SystemFeature.RWXPages;

        public Abi? DefaultAbi { get; }

        // the classifiers are only called for value types
        private static TypeClassification ClassifyX64(Type type, bool isReturn) {
            var size = type.GetManagedSize();
            if (size is 1 or 2 or 4 or 8) {
                return TypeClassification.Register;
            } else {
                return TypeClassification.PointerToMemory;
            }
        }

        public WindowsSystem() {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64) {
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    ClassifyX64,
                    ReturnsReturnBuffer: true);
            } else {
                // TODO: perform selftests here instead of throwing
                //throw new PlatformNotSupportedException($"Windows on non-x86_64 is currently not supported");
            }
        }

        // if the provided backup isn't large enough, the data isn't backed up
        public unsafe void PatchExecutableData(IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup) {
            // TODO: should this be thread-safe? It definitely is not right now.

            // Update the protection of this
            // Because Windows is Windows, we don't actually need to do anything except make sure we're in RWX
            SetProtection(patchTarget, data.Length);

            var target = new Span<byte>((void*) patchTarget, data.Length);
            // now we copy target to backup, then data to target, then flush the instruction cache
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            FlushInstructionCache(patchTarget, data.Length);
        }

        private static void SetProtection(IntPtr addr, nint size) {
            if (!Interop.Windows.VirtualProtect(addr, size, Interop.Windows.PAGE.EXECUTE_READWRITE, out _)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private static void FlushInstructionCache(IntPtr addr, nint size) {
            if (!Interop.Windows.FlushInstructionCache(Interop.Windows.GetCurrentProcess(), addr, size)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private static unsafe Exception LogAllSections(int error, IntPtr src, nint size, [CallerMemberName] string from = "") {
            Exception ex = new Win32Exception(error);
            if (MMDbgLog.Writer == null)
                return ex;

            MMDbgLog.Log($"{from} failed for 0x{src:X16} + {size} - logging all memory sections");
            MMDbgLog.Log($"reason: {ex.Message}");

            try {
                IntPtr addr = (IntPtr) 0x00000000000010000;
                int i = 0;
                while (true) {
                    if (Interop.Windows.VirtualQuery(addr, out Interop.Windows.MEMORY_BASIC_INFORMATION infoBasic, sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) == 0)
                        break;

                    nint srcL = src;
                    nint srcR = srcL + size;
                    nint infoL = (nint) infoBasic.BaseAddress;
                    nint infoR = infoL + (nint) infoBasic.RegionSize;
                    bool overlap = infoL <= srcR && srcL <= infoR;

                    MMDbgLog.Log($"{(overlap ? "*" : "-")} #{i++}");
                    MMDbgLog.Log($"addr: 0x{infoBasic.BaseAddress:X16}");
                    MMDbgLog.Log($"size: 0x{infoBasic.RegionSize:X16}");
                    MMDbgLog.Log($"aaddr: 0x{infoBasic.AllocationBase:X16}");
                    MMDbgLog.Log($"state: {infoBasic.State}");
                    MMDbgLog.Log($"type: {infoBasic.Type}");
                    MMDbgLog.Log($"protect: {infoBasic.Protect}");
                    MMDbgLog.Log($"aprotect: {infoBasic.AllocationProtect}");

                    try {
                        IntPtr addrPrev = addr;
                        addr = unchecked((IntPtr) ((ulong) infoBasic.BaseAddress + (ulong) infoBasic.RegionSize));
                        if ((ulong) addr <= (ulong) addrPrev)
                            break;
                    } catch (OverflowException) {
                        MMDbgLog.Log("overflow");
                        break;
                    }
                }

            } catch {
                throw ex;
            }
            return ex;
        }

        public IMemoryAllocator MemoryAllocator { get; } = new PagedMemoryAllocator();

        private sealed class PagedMemoryAllocator : IMemoryAllocator {

            private sealed class FreeMem {
                public uint BaseOffset;
                public uint Size;
                public FreeMem? NextFree;
            }

            private sealed class PageAlloc : IAllocatedMemory {

                private readonly Page owner;
                private readonly uint offset;

                public PageAlloc(Page page, uint offset, int size) {
                    owner = page;
                    this.offset = offset;
                    Size = size;
                }

                public IntPtr BaseAddress => (IntPtr)(((nint)owner.BaseAddr) + offset);

                public int Size { get; }

                public unsafe Span<byte> Memory => new((void*) BaseAddress, Size);

                #region IDisposable implementation
                private bool disposedValue;

                private void Dispose(bool disposing) {
                    if (!disposedValue) {
                        owner.FreeMem(offset, (uint)Size);
                        disposedValue = true;
                    }
                }

                ~PageAlloc()
                {
                    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                    Dispose(disposing: false);
                }

                public void Dispose() {
                    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                    Dispose(disposing: true);
                    GC.SuppressFinalize(this);
                }
                #endregion
            }

            private sealed class Page {

                private readonly PagedMemoryAllocator owner;

                private readonly object sync = new();

                public readonly IntPtr BaseAddr;
                public readonly uint Size;

                public readonly bool IsExecutable;

                // we keep freeList sorted by BaseOffset, possibly with nulls between
                private FreeMem? freeList;

                public bool IsEmpty => freeList is { } list && list.BaseOffset == 0 && list.Size == Size;

                public Page(PagedMemoryAllocator owner, IntPtr baseAddr, uint size, bool isExecutable) {
                    this.owner = owner;
                    (BaseAddr, Size) = (baseAddr, size);
                    IsExecutable = isExecutable;
                    freeList = new FreeMem {
                        BaseOffset = 0,
                        Size = size,
                        NextFree = null,
                    };
                }

                public bool TryAllocate(uint size, uint align, [MaybeNullWhen(false)] out PageAlloc alloc) {
                    // the sizes we allocate we want to round to a power of two
                    //size = BitOperations.RoundUpToPowerOf2(size);
                    lock (sync) {

                        ref var ptrNode = ref freeList;

                        uint alignOffset = 0;
                        while (ptrNode is not null) {
                            var alignFix = ptrNode.BaseOffset % align;
                            alignFix = alignFix != 0 ? align - alignFix : alignFix;
                            if (ptrNode.Size >= alignFix + size) {
                                alignOffset = alignFix;
                                break; // we found our node
                            }

                            // otherwise, move to the next one
                            ptrNode = ref ptrNode.NextFree;
                        }

                        if (ptrNode is null) {
                            // we couldn't find a free node large enough
                            alloc = null;
                            return false;
                        }

                        var offs = ptrNode.BaseOffset + alignOffset;

                        if (alignOffset == 0) {
                            // if the align offset is zero, then we just allocate out of the front of this node
                            ptrNode.BaseOffset += size;
                            ptrNode.Size -= size;

                            // removing zero-size enties is done in normalize
                        } else {
                            // otherwise, we have to split the free node

                            // create the front half
                            var frontNode = new FreeMem {
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
                        alloc = new PageAlloc(this, offs, (int) size);

                        return true;
                    }
                }

                private void NormalizeFreeList() {
                    ref var node = ref freeList;

                    while (node is not null) {
                        if (node.Size <= 0) {
                            // if the node size is zero, remove it from the list
                            node = node.NextFree;
                            continue; // and retry with new value
                        }

                        if (node.NextFree is { } next &&
                            next.BaseOffset == node.BaseOffset + node.Size) {
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
                public void FreeMem(uint offset, uint size) {
                    lock (sync) {
                        ref var node = ref freeList;

                        while (node is not null) {
                            if (node.BaseOffset > offset) {
                                // we found the first node with greater offset, break out
                                break;
                            }
                            node = ref node.NextFree;
                        }

                        // now node points to where we need to store the new FreeMem, as well as its next node
                        node = new FreeMem {
                            BaseOffset = offset,
                            Size = size,
                            NextFree = node,
                        };
                        NormalizeFreeList();

                        if (IsEmpty) {
                            owner.RegisterForCleanup(this);
                        }
                    }
                }
            }

            private readonly nint pageBaseMask;
            private readonly nint pageSize;
            private readonly bool pageSizeIsPow2;

            public PagedMemoryAllocator() {
                Interop.Windows.GetSystemInfo(out var sysInfo);

                pageSize = (nint) sysInfo.dwPageSize;

                pageSizeIsPow2 = (pageSize & (pageSize - 1)) == 0; // BitOperations.IsPow2, because .NET 5 doesn't expose that
                pageBaseMask = (~(nint) 0) << BitOperations.TrailingZeroCount(pageSize);
            }

            private nint RoundDownToPageBoundary(nint ptr) {
                if (pageSizeIsPow2) {
                    return ptr & pageBaseMask;
                } else {
                    return ptr - (ptr % ((nint)pageSize));
                }
            }

            private Page?[] allocationList = new Page?[16];
            private int pageCount;

            private readonly struct PageComparer : IComparer<Page?> {
                public int Compare(Page? x, Page? y) {
                    if (x == y)
                        return 0;
                    if (x is null)
                        return 1;
                    if (y is null)
                        return -1;

                    return ((long) x.BaseAddr).CompareTo((long) y.BaseAddr);
                }
            }

            private readonly struct PageAddrComparable : IComparable<Page> {
                private readonly IntPtr addr;
                public PageAddrComparable(IntPtr addr) => this.addr = addr;
                public int CompareTo(Page? other) {
                    if (other is null)
                        return 1;

                    return ((long) addr).CompareTo((long) other.BaseAddr);
                }
            }

            // .NET 5 doesn't expose BitOperations.RoundUpToPowerOf2
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            [CLSCompliant(false)]
            public static uint RoundUpToPowerOf2(uint value) {
                // Based on https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
                --value;
                value |= value >> 1;
                value |= value >> 2;
                value |= value >> 4;
                value |= value >> 8;
                value |= value >> 16;
                return value + 1;
            }

            private void InsertAllocatedPage(Page page) {
                if (pageCount == allocationList.Length) {
                    // we need to expand the allocationList
                    var newSize = (int) RoundUpToPowerOf2((uint) allocationList.Length);
                    Array.Resize(ref allocationList, newSize);
                }

                var list = allocationList.AsSpan();

                var insertAt = list.Slice(0, pageCount).BinarySearch(page, new PageComparer());

                if (insertAt >= 0) {
                    // the page is already in the list, no work needed
                    return;
                } else {
                    insertAt = ~insertAt;
                }

                // insertAt is the index of the next item larger, which is the index we want the page to be at
                list.Slice(insertAt, pageCount - insertAt).CopyTo(list.Slice(insertAt + 1));
                list[insertAt] = page;
                pageCount++;
            }

            private void RemoveAllocatedPage(Page page) {
                var list = allocationList.AsSpan();

                var indexToRemove = list.Slice(0, pageCount).BinarySearch(page, new PageComparer());

                if (indexToRemove < 0) {
                    // the page doesn't exist, nothing needs to be done
                    return;
                }

                // just copy from above the index down
                list.Slice(indexToRemove + 1).CopyTo(list.Slice(indexToRemove));
                pageCount--;
            }

            private ReadOnlySpan<Page> AllocList => allocationList.AsSpan().Slice(0, pageCount);

            private int GetBoundIndex(IntPtr ptr) {
                var index = AllocList.BinarySearch(new PageAddrComparable(ptr));

                return index >= 0 ? index : ~index;
            }

            private readonly ConcurrentBag<Page> pagesToClean = new();
            private int registeredForCleanup;
            private void RegisterForCleanup(Page page) {
                pagesToClean.Add(page);

                if (Interlocked.CompareExchange(ref registeredForCleanup, 1, 0) == 0) {
                    Gen2GcCallback.Register(DoCleanup);
                }
            }

            private bool DoCleanup() {
                Volatile.Write(ref registeredForCleanup, 0);

                while (pagesToClean.TryTake(out var page)) {
                    lock (sync) {
                        // if the page is no longer empty, don't free
                        if (!page.IsEmpty)
                            continue;

                        // otherwise, remove it from the allocation list, so we can free it outside the lock
                        RemoveAllocatedPage(page);
                    }

                    // now we can actually free the associated memory
                    if (!Interop.Windows.VirtualFree(page.BaseAddr, 0, Interop.Windows.MEM.RELEASE)) {
                        // VirtualFree failed, what? log the error and move on
                        MMDbgLog.Log($"VirtualFree failed trying to deallocate a page! {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    }
                }

                // this should never be re-registered for later GCs, at least until another page is marked as needing cleaning
                return false;
            }

            private readonly object sync = new();

            public bool TryAllocateInRange(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                if ((nint) request.LowBound > request.HighBound)
                    throw new ArgumentException("Low and High are reversed", nameof(request));
                if ((nint) request.Target < request.LowBound || (nint) request.Target > request.HighBound)
                    throw new ArgumentException("Target not between low and high", nameof(request));

                if (request.Size < 0)
                    throw new ArgumentException("Size is negative", nameof(request));
                if (request.Alignment <= 0)
                    throw new ArgumentException("Alignment is zero or negative", nameof(request));

                if (request.Size > pageSize) {
                    // TODO: large allocations
                    throw new NotSupportedException("Single allocations cannot be larger than a page");
                }

                // we want to round the low value up
                var lowPageBound = RoundDownToPageBoundary(request.LowBound + pageSize - 1);
                // and the high value down
                var highPageBound = RoundDownToPageBoundary(request.HighBound);

                var targetPage = RoundDownToPageBoundary(request.Target);

                var target = (nint) request.Target;

                lock (sync) {
                    var lowIdxBound = GetBoundIndex(lowPageBound);
                    var highIdxBound = GetBoundIndex(highPageBound);

                    if (lowIdxBound != highIdxBound) {
                        // there are pages for us to check within the target range
                        // lets see where our target lands us
                        var targetIndex = GetBoundIndex(targetPage);

                        // we'll check pages starting at targetIndex and expanding around it

                        var lowIdx = targetIndex - 1;
                        var highIdx = targetIndex;

                        while (lowIdx >= lowIdxBound || highIdx < highIdxBound) {
                            // try high pages, while they're closer than low pages
                            while (
                                highIdx < highIdxBound && 
                                (lowIdx < lowIdxBound || target - AllocList[lowIdx].BaseAddr < AllocList[highIdx].BaseAddr - target)
                            ) {
                                if (TryAllocWithPage(AllocList[highIdx], request, out allocated))
                                    return true;
                                highIdx++;
                            }

                            // then try low pages, while they're closer than high pages
                            while (
                                lowIdx >= lowIdxBound &&
                                (highIdx >= highIdxBound || target - AllocList[lowIdx].BaseAddr > AllocList[highIdx].BaseAddr - target)
                            ) {
                                if (TryAllocWithPage(AllocList[lowIdx], request, out allocated))
                                    return true;
                                lowIdx++;
                            }
                        }

                        // if we fall out here, no adequate page was found
                    }

                    // if we make it here, we need to allocate a page from the OS

                    // we'll do the same approach for trying to find an existing page, but querying the OS for free pages to allocate

                    var lowPage = targetPage - pageSize;
                    var highPage = targetPage;

                    while (lowPage >= lowPageBound || highPage < highPageBound) {
                        // first check the high pages, while they're closer than low pages
                        while (
                            highPage < highPageBound &&
                            (lowPage < lowPageBound || target - lowPage < highPage - target)
                        ) {
                            if (TryAllocNewPage(request, ref highPage, true, out allocated))
                                return true;
                        }

                        // then try low pages, while they're closer than high pages
                        while (
                            lowPage >= lowPageBound &&
                            (highPage >= highPageBound || target - lowPage > highPage - target)
                        ) {
                            if (TryAllocNewPage(request, ref lowPage, false, out allocated))
                                return true;
                        }
                    }

                    // if we fall out to here, we just couldn't allocate, so sucks
                    allocated = null;
                    return false;
                }
            }

            private static bool TryAllocWithPage(Page page, AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                if (page.IsExecutable == request.Executable && page.BaseAddr >= (nint) request.LowBound && page.BaseAddr < (nint) request.HighBound) {
                    if (page.TryAllocate((uint) request.Size, (uint) request.Alignment, out var pageAlloc)) {
                        if ((nint) pageAlloc.BaseAddress >= request.LowBound && (nint) pageAlloc.BaseAddress < request.HighBound) {
                            // we've found a valid allocation, we're done
                            allocated = pageAlloc;
                            return true;
                        } else {
                            // this allocation isn't within the bounds (not sure how this could happen, but check anyway)
                            // we deallocate by disposing, and move on
                            pageAlloc.Dispose();
                        }
                    }
                }

                allocated = null;
                return false;
            }

            private unsafe bool TryAllocNewPage(AllocationRequest request, ref nint page, bool goingUp, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {

                if (Interop.Windows.VirtualQuery(page, out var memInfo, sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) != 0) {
                    // VirtualQuery succeeded, lets check it out

                    if (memInfo.State != Interop.Windows.MEM.FREE) {
                        // this is not a free block, we don't care about it
                        goto Fail;
                    }

                    var pageProt = request.Executable ? Interop.Windows.PAGE.EXECUTE_READWRITE : Interop.Windows.PAGE.READWRITE;

                    // page points at a free page, lets allocate!
                    var allocBase = Interop.Windows.VirtualAlloc(page, pageSize, Interop.Windows.MEM.RESERVE | Interop.Windows.MEM.COMMIT, pageProt);
                    if (allocBase == IntPtr.Zero) {
                        // VirtualAlloc failed, fail out
                        // TODO: check GetLastError
                        goto Fail;
                    }

                    // we have a page allocated now, lets create a Page and insert it
                    var pageObj = new Page(this, allocBase, (uint) pageSize, request.Executable);
                    InsertAllocatedPage(pageObj);

                    // now we'll try to allocate out of the page. If we fail, we'll immediately register it for cleanup.
                    if (!pageObj.TryAllocate((uint) request.Size, (uint) request.Alignment, out var alloc)) {
                        RegisterForCleanup(pageObj);
                        goto Fail;
                    }

                    // if we successfully allocated, return the page allocation
                    allocated = alloc;
                    return true;

                    Fail:
                    // We're failing out, update the page address appropriately
                    if (goingUp) {
                        page = (nint) memInfo.AllocationBase + memInfo.RegionSize;
                    } else {
                        page = memInfo.AllocationBase - pageSize;
                    }

                    allocated = null;
                    return false;
                } else {
                    // TODO: check GetLastError

                    // query failed, fail out
                    if (goingUp) {
                        page += pageSize;
                    } else {
                        page -= pageSize;
                    }
                    allocated = null;
                    return false;
                }
            }
        }
    }
}
