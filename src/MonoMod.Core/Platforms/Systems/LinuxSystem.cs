using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms.Systems {
    internal class LinuxSystem : ISystem {
        public OSKind Target => OSKind.Linux;

        public SystemFeature Features => SystemFeature.RWXPages | SystemFeature.RXPages;

        private readonly Abi defaultAbi;
        public Abi? DefaultAbi => defaultAbi;

        private readonly nint PageSize;

        private readonly MmapPagedMemoryAllocator allocator;
        public IMemoryAllocator MemoryAllocator => allocator;

        public static TypeClassification ClassifyAMD64(Type type, bool isReturn) {
            var totalSize = type.GetManagedSize();
            if (totalSize > 64 || totalSize % 2 == 1) return TypeClassification.OnStack;
            return TypeClassification.InRegister;
        }

        public LinuxSystem() {
            PageSize = (nint)Unix.Sysconf(Unix.SysconfName.PageSize);
            allocator = new MmapPagedMemoryAllocator(PageSize);

            if (PlatformDetection.Architecture == ArchitectureKind.x86_64) {
                defaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    ClassifyAMD64,
                    true
                );
            } else {
                throw new NotImplementedException();
            }
        }

        public nint GetSizeOfReadableMemory(IntPtr start, nint guess) {
            nint currentPage = allocator.RoundDownToPageBoundary(start);
            if (!allocator.PageAllocated(currentPage)) {
                return 0;
            }
            currentPage += PageSize;
            
            nint known = currentPage - start;

            while (known < guess) {
                if (!allocator.PageAllocated(currentPage)) {
                    return known;
                }
                known += PageSize;
                currentPage += PageSize;
            }

            return known;
        }

        public unsafe void PatchData(PatchTargetKind patchKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup) {
            // TODO: should this be thread-safe? It definitely is not right now.

            // Update the protection of this
            if (patchKind == PatchTargetKind.Executable) {
                // Because Windows is Windows, we don't actually need to do anything except make sure we're in RWX
                ProtectRWX(patchTarget, data.Length);
            } else {
                ProtectRW(patchTarget, data.Length);
            }

            var target = new Span<byte>((void*) patchTarget, data.Length);
            // now we copy target to backup, then data to target, then flush the instruction cache
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);
        }

        private void RoundToPageBoundary(ref nint addr, ref nint size) {
            var newAddr = allocator.RoundDownToPageBoundary(addr);
            size += addr - newAddr;
            addr = newAddr;
        }

        private void ProtectRW(IntPtr addr, nint size) {
            RoundToPageBoundary(ref addr, ref size);
            if (Unix.Mprotect(addr, (nuint) size, Unix.Protection.Read | Unix.Protection.Write) != 0) {
                throw new Win32Exception();
            }
        }

        private void ProtectRWX(IntPtr addr, nint size) {
            RoundToPageBoundary(ref addr, ref size);
            if (Unix.Mprotect(addr, (nuint) size, Unix.Protection.Read | Unix.Protection.Write | Unix.Protection.Execute) != 0) {
                throw new Win32Exception();
            }
        }

        private sealed class MmapPagedMemoryAllocator : PagedMemoryAllocator {
            public MmapPagedMemoryAllocator(nint pageSize)
                : base(pageSize) {
            }

            [SuppressMessage("Design", "CA1032:Implement standard exception constructors")]
            [SuppressMessage("Design", "CA1064:Exceptions should be public",
                Justification = "This is used exclusively internally as jank control flow because I'm lazy")]
            private sealed class SyscallNotImplementedException : Exception { }

            public unsafe bool PageAllocated(nint page) {
                byte garbage;
                // TODO: Mincore isn't implemented in WSL, and always gives ENOSYS
                if (Unix.Mincore(page, 1, &garbage) == -1) {
                    var lastError = Marshal.GetLastWin32Error();
                    if (lastError == 12) {  // ENOMEM, page is unallocated
                        return false;
                    }
                    if (lastError == 38) { // ENOSYS Function not implemented
                        // TODO: possibly implement /proc/self/maps parsing as a fallback
                        throw new SyscallNotImplementedException();
                    }
                    throw new NotImplementedException($"Got unimplemented errno for mincore(2); errno = {lastError}");
                }
                return true;
            }

            private bool canTestPageAllocation = true;

            protected override bool TryAllocateNewPage(
                AllocationRequest request,
                nint targetPage, nint lowPageBound, nint highPageBound,
                [MaybeNullWhen(false)] out IAllocatedMemory allocated
            ) {
                if (!canTestPageAllocation) {
                    allocated = null;
                    return false;
                }

                var prot = request.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;
                
                // number of pages needed to satisfy length requirements
                nint numPages = request.Size / PageSize + 1;
                
                // find the nearest unallocated page within our bounds
                nint low = targetPage - PageSize;
                nint high = targetPage;
                nint ptr = -1;

                try {
                    while (low >= lowPageBound || high <= highPageBound) {

                        // check above the target page first
                        if (high <= highPageBound) {
                            for (nint i = 0; i < numPages; i++) {
                                if (PageAllocated(high + PageSize * i)) {
                                    high += PageSize;
                                    goto FailHigh;
                                }
                            }
                            // all pages are unallocated, we're done
                            ptr = high;
                            break;
                        }
                        FailHigh:
                        if (low >= lowPageBound) {
                            for (nint i = 0; i < numPages; i++) {
                                if (PageAllocated(low + PageSize * i)) {
                                    low -= PageSize;
                                    goto FailLow;
                                }
                            }
                            // all pages are unallocated, we're done
                            ptr = low;
                            break;
                        }
                        FailLow:
                        { }
                    }
                } catch (SyscallNotImplementedException) {
                    canTestPageAllocation = false;
                    allocated = null;
                    return false;
                }

                // unable to find a page within bounds
                if (ptr == -1) {
                    allocated = null;
                    return false;
                }
                
                // mmap the page we found
                nint mmapPtr = Unix.Mmap(ptr, (nuint)request.Size, prot, Unix.MmapFlags.Anonymous | Unix.MmapFlags.FixedNoReplace, -1, 0);
                if (mmapPtr == 0) {
                    // fuck
                    allocated = null;
                    return false;
                }

                // create a Page object for the newly mapped memory, even before deciding whether we succeeded or not
                var page = new Page(this, mmapPtr, (uint) PageSize, request.Executable);
                InsertAllocatedPage(page);

                // for simplicity, we'll try to allocate out of the page before checking bounds
                if (!page.TryAllocate((uint) request.Size, (uint) request.Alignment, out var pageAlloc)) {
                    // huh???
                    RegisterForCleanup(page);
                    allocated = null;
                    return false;
                }

                if ((nint) pageAlloc.BaseAddress < request.LowBound || (nint) pageAlloc.BaseAddress + pageAlloc.Size >= request.HighBound) {
                    // the allocation didn't land in bounds, fail out
                    pageAlloc.Dispose(); // because this is the only allocation in the page, this auto-registers it for cleanup
                    allocated = null;
                    return false;
                }

                // we got an allocation!
                allocated = pageAlloc;
                return true;
            }

            protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg) {
                var res = Unix.Munmap(page.BaseAddr, page.Size);
                if (res != 0) {
                    errorMsg = new Win32Exception().Message;
                    return false;
                }
                errorMsg = null;
                return true;
            }
        }
    }
}
