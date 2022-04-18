using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

        private class PagedMemoryAllocator : IMemoryAllocator {

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
                            var alignFix = align - (ptrNode.BaseOffset % align);
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

                        if (freeList is { } list && list.BaseOffset == 0 && list.Size == Size) {
                            // TODO: register for page free
                        }
                    }
                }
            }

            private readonly nint pageBaseMask;
            private readonly uint pageSize;
            private readonly bool pageSizeIsPow2;

            public PagedMemoryAllocator() {
                Interop.Windows.GetSystemInfo(out var sysInfo);

                pageSize = sysInfo.dwPageSize;

                pageSizeIsPow2 = BitOperations.IsPow2(pageSize);
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

            private void InsertAllocatedPage(Page page) {
                if (pageCount == allocationList.Length) {
                    // we need to expand the allocationList
                    var newSize = (int) BitOperations.RoundUpToPowerOf2((uint) allocationList.Length);
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

            private readonly object sync = new();

            public bool TryAllocateInRange(nint target, nint low, nint high, int size, int align, bool executable, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                if (low > high)
                    throw new ArgumentException("Low and High are reversed", nameof(low));
                if (target < low || target > high)
                    throw new ArgumentException("Target not between low and high", nameof(target));

                if (size < 0)
                    throw new ArgumentException("Size is negative", nameof(size));
                if (align <= 0)
                    throw new ArgumentException("Alignment is zero or negative", nameof(align));

                if (size > pageSize) {
                    // TODO: large allocations
                    throw new NotSupportedException("Single allocations cannot be larger than a page");
                }

                // we want to round the low value up
                var pageRoundedLow = RoundDownToPageBoundary(low + (nint)pageSize - 1);
                // and the high value down
                var pageRoundedHigh = RoundDownToPageBoundary(high);

                lock (sync) {
                    var range = high - low;
                    var lowDiff = target - low;
                    var highDiff = high - target;

                    var lowIdxBound = GetBoundIndex(pageRoundedLow);
                    var highIdxBound = GetBoundIndex(pageRoundedHigh);

                    if (lowIdxBound != highIdxBound) {
                        // there are pages for us to check within the target range
                        // lets see where our target lands us
                        var targetIndex = GetBoundIndex(RoundDownToPageBoundary(target));

                        // we'll check pages starting at targetIndex and expanding around it

                        var lowIdx = targetIndex - 1;
                        var highIdx = targetIndex;

                        while (lowIdx >= lowIdxBound || highIdx < highIdxBound) {
                            // try high pages, while they're closer than low pages
                            while (
                                highIdx < highIdxBound && 
                                (lowIdx < lowIdxBound || target - AllocList[lowIdx].BaseAddr < AllocList[highIdx].BaseAddr - target)
                            ) {
                                if (TryAllocWithPage(AllocList[highIdx], low, high, size, align, executable, out allocated))
                                    return true;
                                highIdx++;
                            }

                            // then try low pages, while they're closer than high pages
                            while (
                                lowIdx >= lowIdxBound &&
                                (highIdx >= highIdxBound || target - AllocList[lowIdx].BaseAddr > AllocList[highIdx].BaseAddr - target)
                            ) {
                                if (TryAllocWithPage(AllocList[lowIdx], low, high, size, align, executable, out allocated))
                                    return true;
                                lowIdx++;
                            }
                        }

                        // if we fall out here, no adequate page was found
                    }

                    // TODO: allocate new pages from the OS

                    throw new NotImplementedException();
                }


                static bool TryAllocWithPage(Page page, nint low, nint high, int size, int align, bool exec, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                    if (page.IsExecutable == exec && page.BaseAddr >= low && page.BaseAddr < high) {
                        if (page.TryAllocate((uint) size, (uint) align, out var pageAlloc)) {
                            if (pageAlloc.BaseAddress >= low && pageAlloc.BaseAddress < high) {
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
            }
        }
    }
}
