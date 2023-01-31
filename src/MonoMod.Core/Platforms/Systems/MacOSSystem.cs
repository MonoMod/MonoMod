using MonoMod.Core.Platforms.Memory;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using static MonoMod.Core.Interop.OSX;

namespace MonoMod.Core.Platforms.Systems {
    internal class MacOSSystem : ISystem {
        public OSKind Target => OSKind.OSX;

        public SystemFeature Features => SystemFeature.RXPages | SystemFeature.RWXPages;

        public Abi? DefaultAbi => default; // TODO:

        // TODO: MacOS needs a native exception helper; implement it
        public INativeExceptionHelper? NativeExceptionHelper => null;

        public unsafe IEnumerable<string?> EnumerateLoadedModuleFiles() {
            var infoCnt = task_dyld_info.Count;
            var kr = task_info(mach_task_self(), task_flavor_t.DyldInfo, out var dyldInfo, ref infoCnt);
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

            var selfTask = mach_task_self();
            var origAddr = (ulong) start;
            var addr = origAddr;
            do {
                var infoCount = sizeof(Interop.OSX.vm_region_basic_info_64) / sizeof(int);
                if (!mach_vm_region(selfTask, ref addr, out var size, vm_region_flavor_t.BasicInfo64, out var info, ref infoCount, out _)) {
                    return knownSize;
                }

                if (addr > origAddr) // the page returned is further above
                    return knownSize;

                var isReadable = (info.protection & vm_prot_t.Read) != 0;
                if (!isReadable)
                    return knownSize;

                knownSize += (nint)(addr + size - origAddr);
                origAddr = addr + size;
                addr = origAddr;
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

            if (!memIsExec) {
                // our target memory is not executable
                if (!memIsWrite) {
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
                }

                // now we copy target to backup, then data to target
                var target = new Span<byte>((void*) patchTarget, data.Length);
                _ = target.TryCopyTo(backup);
                data.CopyTo(target);
            } else {
                // TODO: implement patching executable memory
                // TODO: how do we detect whether a region has MAP_JIT? (if MAP_JIT even exists on this system?)
                throw new NotImplementedException();
            }
        }

        private static unsafe bool TryGetProtForMem(nint startAddr, int length, out vm_prot_t maxProt, out vm_prot_t prot, out bool crossesAllocBoundary, out bool notAllocated) {
            maxProt = (vm_prot_t) (-1);
            prot = (vm_prot_t) (-1);

            var selfTask = mach_task_self();
            crossesAllocBoundary = false;
            notAllocated = false;

            var addr = (ulong) startAddr;

            do {
                if (addr >= (ulong)(startAddr + length))
                    break;

                var origAddr = addr;
                var infoCount = vm_region_basic_info_64.Count;
                var kr = mach_vm_region(selfTask, ref addr, out var allocSize, vm_region_flavor_t.BasicInfo64, out var info, ref infoCount, out _);
                if (kr) {
                    if (addr > origAddr) {
                        // the address isn't allocated, and it returned the next region
                        notAllocated = true;
                        return false;
                    }

                    // if our region crosses alloc boundaries, we return the union of all prots
                    prot &= info.protection;
                    maxProt &= info.max_protection;
                    
                    addr += allocSize;

                    if (addr < (ulong)(startAddr + length)) {
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

        public IMemoryAllocator MemoryAllocator { get; } = new QueryingPagedMemoryAllocator(new PageAllocator());

        private sealed class PageAllocator : QueryingMemoryPageAllocatorBase {
            private readonly uint pageSize;
            public override uint PageSize => pageSize;

            public PageAllocator() {
                pageSize = (uint) GetPageSize();
            }

            public override bool TryAllocatePage(nint size, bool executable, out IntPtr allocated) {
                allocated = default;
                return false;
            }

            public override bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated) {
                allocated = default;
                return false;
            }

            public override bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg) {
                errorMsg = "Not yet implemented";
                return false;
            }

            public override bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize) {
                isFree = false;
                allocBase = default;
                allocSize = default;
                return false;
            }
        }
    }
}
