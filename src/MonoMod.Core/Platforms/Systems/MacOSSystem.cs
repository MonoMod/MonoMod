using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using static MonoMod.Core.Interop.OSX;

namespace MonoMod.Core.Platforms.Systems {
    internal class MacOSSystem : ISystem {
        public OSKind Target => OSKind.OSX;

        public SystemFeature Features => SystemFeature.RXPages | SystemFeature.RWXPages;

        public Abi? DefaultAbi { get; }

        public MacOSSystem() {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64) {
                // As best I can find (Apple docs are worthless) MacOS uses SystemV on x64
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    SystemVABI.ClassifyAMD64,
                    true
                );
            } else {
                throw new NotImplementedException();
            }
        }

        // TODO: MacOS needs a native exception helper; implement it
        public INativeExceptionHelper? NativeExceptionHelper => null;

        public unsafe IEnumerable<string?> EnumerateLoadedModuleFiles() {
            var infoCnt = task_dyld_info.Count;
            var dyldInfo = default(task_dyld_info);
            var kr = task_info(mach_task_self(), task_flavor_t.DyldInfo, &dyldInfo, &infoCnt);
            if (!kr) {
                return ArrayEx.Empty<string>(); // could not get own dyld info
            }

            var infos = dyldInfo.all_image_infos->InfoArray;

            var arr = new string?[infos.Length];
            for (var i = 0; i < arr.Length; i++) {
                arr[i] = infos[i].imageFilePath.ToString();
            }

            return arr;
        }

        public unsafe nint GetSizeOfReadableMemory(IntPtr start, nint guess) {
            nint knownSize = 0;

            do {
                if (!GetLocalRegionInfo(start, out var realStart, out var realSize, out var prot, out _)) {
                    return knownSize;
                }

                if (realStart > start) // the page returned is further above
                    return knownSize;

                var isReadable = (prot & vm_prot_t.Read) != 0;
                if (!isReadable)
                    return knownSize;

                knownSize += realStart + realSize - start;
                start = realStart + realSize;
            } while (knownSize < guess);

            return knownSize;
        }

        public unsafe void PatchData(PatchTargetKind targetKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup) {

            // targetKind is a hint for what the caller believes the memory to be. Because MacOS is more strict than Linux or Windows,
            // we need to actually check that to behave correctly in all cases.

            var selfTask = mach_task_self();
            var len = data.Length;

            bool memIsRead;
            bool memIsWrite;
            bool memIsExec;

            // we assume these defaults; this may end up blowing up completely
            var canMakeRead = true;
            var canMakeWrite = true;
            var canMakeExec = true;

            if (TryGetProtForMem(patchTarget, len, out var maxProt, out var curProt, out var crossesBoundary, out var notAllocated)) {
                if (crossesBoundary) {
                    MMDbgLog.Warning($"Patch requested for memory which spans multiple memory allocations. Failures may result. (0x{patchTarget:x16} length {len})");
                }

                memIsRead = curProt.Has(vm_prot_t.Read);
                memIsWrite = curProt.Has(vm_prot_t.Write);
                memIsExec = curProt.Has(vm_prot_t.Execute);
                canMakeRead = maxProt.Has(vm_prot_t.Read);
                canMakeWrite = maxProt.Has(vm_prot_t.Write);
                canMakeExec = maxProt.Has(vm_prot_t.Execute);
            } else {
                // we couldn't get prot info
                // was it because the region wasn't allocated (in part or in full)?
                if (notAllocated) {
                    MMDbgLog.Error($"Requested patch of region which was not fully allocated (0x{patchTarget:x16} length {len})");
                    throw new InvalidOperationException("Cannot patch unallocated region"); // TODO: is there a better exception for this?
                }
                // otherwise, assume based on what the caller gave us
                memIsRead = true;
                memIsWrite = false;
                memIsExec = targetKind is PatchTargetKind.Executable;
            }

            // We know know what protections the target memory region has, so we can decide on a course of action.

            if (!memIsWrite) {
                if (!memIsExec) {
                    // our target memory is not executable
                    // if the memory is not currently writable, make it writable
                    if (!canMakeWrite) {
                        // TODO: figure out a workaround for this
                        MMDbgLog.Error($"Requested patch of region which cannot be made writable (cur prot: {curProt}, max prot: {maxProt}, None means failed to get info) (0x{patchTarget:x16} length {len})");
                        throw new InvalidOperationException("Requested patch region cannot be made writable");
                    }

                    // we should be able to make the region writable, after which we can just copy data in using spans
                    var kr = mach_vm_protect(selfTask, (ulong) patchTarget, (ulong) len, false, vm_prot_t.Read | vm_prot_t.Write);
                    if (!kr) {
                        if (kr == kern_return_t.ProtectionFailure) {
                            MMDbgLog.Error($"Protection failure trying to make (0x{patchTarget:x16} length {len}) writable (how?)");
                        }
                        throw new InvalidOperationException($"Unable to make region writable (kr = {kr.Value})");
                    }
                } else {
                    // TODO: implement patching executable memory
                    // TODO: how do we detect whether a region has MAP_JIT? (if MAP_JIT even exists on this system?)
                    throw new NotImplementedException();
                }
            }

            // at this point, we know our data to be writable

            // now we copy target to backup, then data to target
            var target = new Span<byte>((void*) patchTarget, data.Length);
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            // if we got here when executable (either because the memory was already writable or we were able to make it writable) we need to flush the icache
            if (memIsExec) {
                sys_icache_invalidate((void*) patchTarget, (nuint) data.Length);
            }
        }

        private static unsafe bool TryGetProtForMem(nint addr, int length, out vm_prot_t maxProt, out vm_prot_t prot, out bool crossesAllocBoundary, out bool notAllocated) {
            maxProt = (vm_prot_t) (-1);
            prot = (vm_prot_t) (-1);

            crossesAllocBoundary = false;
            notAllocated = false;

            var origAddr = addr;

            do {
                if (addr >= origAddr + length)
                    break;

                // TODO: use mach_vm_region_recurse directly to enumerate consecutive regions sanely
                var kr = GetLocalRegionInfo(addr, out var startAddr, out var realSize, out var iprot, out var iMaxProt);
                if (kr) {
                    if (startAddr > addr) {
                        // the address isn't allocated, and it returned the next region
                        notAllocated = true;
                        return false;
                    }

                    // if our region crosses alloc boundaries, we return the union of all prots
                    prot &= iprot;
                    maxProt &= iMaxProt;

                    addr = startAddr + realSize;

                    if (addr < origAddr + length) {
                        // the end of this alloc is before the end of the requrested region, so we cross a boundary
                        crossesAllocBoundary = true;
                        continue;
                    }
                } else {
                    if (kr == kern_return_t.NoSpace) {
                        // the address isn't allocated, and there is no region higher
                        notAllocated = true;
                        return false;
                    }

                    // otherwise, request failed for unknown reason
                    return false;
                }

                // if we ever get here, break out
                break;
            }
            while (true);

            return true;
        }

        // this is based loosely on https://stackoverflow.com/questions/6963625/mach-vm-region-recurse-mapping-memory-and-shared-libraries-on-osx
        private static unsafe kern_return_t GetLocalRegionInfo(nint origAddr, out nint startAddr, out nint outSize, out vm_prot_t prot, out vm_prot_t maxProt) {
            kern_return_t kr;
            ulong size;
            var depth = int.MaxValue;

            vm_region_submap_short_info_64 info;
            var count = vm_region_submap_short_info_64.Count;
            var addr = (ulong) origAddr;
            kr = mach_vm_region_recurse(mach_task_self(), &addr, &size, &depth, &info, &count);
            if (!kr) {
                startAddr = default;
                outSize = default;
                prot = default;
                maxProt = default;
                return kr;
            }

            Helpers.Assert(!info.is_submap);
            startAddr = (nint) addr;
            outSize = (nint) size;
            prot = info.protection;
            maxProt = info.max_protection;
            return kr;
        }

        public IMemoryAllocator MemoryAllocator { get; } = new VmAllocPagedMemoryAllocator(GetPageSize());

        private sealed class VmAllocPagedMemoryAllocator : PagedMemoryAllocator {
            public VmAllocPagedMemoryAllocator(nint pageSize)
                : base(pageSize) {
            }

            public static unsafe bool PageAllocated(nint page) {
                var kr = GetLocalRegionInfo(page, out var addr, out _, out _, out _);
                if (kr) {
                    if (addr > page) {
                        return false;
                    } else {
                        return true;
                    }
                } else {
                    return false;
                }
            }

            protected override unsafe bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                var prot = request.Executable ? vm_prot_t.Execute : vm_prot_t.None;
                prot |= vm_prot_t.Read | vm_prot_t.Write;

                // map the page
                var addr = 0uL;
                var kr = mach_vm_allocate(mach_task_self(), &addr, (ulong) PageSize, vm_flags.Anywhere);
                if (!kr) {
                    MMDbgLog.Error($"Error creating allocation: kr = {kr.Value}");
                    allocated = null;
                    return false;
                }

                // TODO: handle execute protections better
                kr = mach_vm_protect(mach_task_self(), addr, (ulong) PageSize, false, prot);
                if (!kr) {
                    MMDbgLog.Error($"Could not set protections on newly created allocation: kr = {kr.Value}");
                    _ = mach_vm_deallocate(mach_task_self(), addr, (ulong) PageSize);
                    allocated = null;
                    return false;
                }

                var mmapPtr = (nint) addr;

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

                // we got an allocation!
                allocated = pageAlloc;
                return true;
            }

            protected override unsafe bool TryAllocateNewPage(
                PositionedAllocationRequest request,
                nint targetPage, nint lowPageBound, nint highPageBound,
                [MaybeNullWhen(false)] out IAllocatedMemory allocated
            ) {
                var prot = request.Base.Executable ? vm_prot_t.Execute : vm_prot_t.None;
                prot |= vm_prot_t.Read | vm_prot_t.Write;

                // number of pages needed to satisfy length requirements
                var numPages = request.Base.Size / PageSize + 1;
                Helpers.Assert(numPages == 1);

                // find the nearest unallocated page within our bounds
                var low = targetPage - PageSize;
                var high = targetPage;
                nint ptr = -1;

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

                // unable to find a page within bounds
                if (ptr == -1) {
                    allocated = null;
                    return false;
                }

                var addr = (ulong) ptr;
                var kr = mach_vm_allocate(mach_task_self(), &addr, (ulong) PageSize, vm_flags.Fixed);
                if (!kr) {
                    MMDbgLog.Error($"Error creating allocation: kr = {kr.Value}");
                    allocated = null;
                    return false;
                }

                // TODO: handle execute protections better
                kr = mach_vm_protect(mach_task_self(), addr, (ulong) PageSize, false, prot);
                if (!kr) {
                    MMDbgLog.Error($"Could not set protections on newly created allocation: kr = {kr.Value}");
                    _ = mach_vm_deallocate(mach_task_self(), addr, (ulong) PageSize);
                    allocated = null;
                    return false;
                }

                var mmapPtr = (nint) addr;
                // create a Page object for the newly mapped memory, even before deciding whether we succeeded or not
                var page = new Page(this, mmapPtr, (uint) PageSize, request.Base.Executable);
                InsertAllocatedPage(page);

                // for simplicity, we'll try to allocate out of the page before checking bounds
                if (!page.TryAllocate((uint) request.Base.Size, (uint) request.Base.Alignment, out var pageAlloc)) {
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
                var kr = mach_vm_deallocate(mach_task_self(), (ulong) page.BaseAddr, page.Size);
                if (!kr) {
                    errorMsg = $"Failure to deallocate page: kr = {kr.Value}";
                    return false;
                }
                errorMsg = null;
                return true;
            }
        }
    }
}
