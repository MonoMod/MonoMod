using MonoMod.Core.Platforms.Memory;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms.Systems {
    internal class MacOSSystem : ISystem {
        public OSKind Target => OSKind.OSX;

        public SystemFeature Features => SystemFeature.RXPages | SystemFeature.RWXPages;

        public Abi? DefaultAbi => default; // TODO:

        public INativeExceptionHelper? NativeExceptionHelper => throw new NotImplementedException();

        public unsafe IEnumerable<string?> EnumerateLoadedModuleFiles() {
            var infoCnt = Interop.OSX.task_dyld_info.Count;
            var kr = Interop.OSX.task_info(Interop.OSX.mach_task_self(), Interop.OSX.task_flavor_t.DyldInfo, out var dyldInfo, ref infoCnt);
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

            var selfTask = Interop.OSX.mach_task_self();
            var origAddr = (ulong) start;
            var addr = origAddr;
            do {
                var infoCount = sizeof(Interop.OSX.vm_region_basic_info_64) / sizeof(int);
                if (!Interop.OSX.mach_vm_region(selfTask, ref addr, out var size, Interop.OSX.vm_region_flavor_t.BasicInfo64, out var info, ref infoCount, out _)) {
                    return knownSize;
                }

                if (addr > origAddr) // the page returned is further above
                    return knownSize;

                var isReadable = (info.protection & Interop.OSX.vm_prot_t.Read) != 0;
                if (!isReadable)
                    return knownSize;

                knownSize += (nint)(addr + size - origAddr);
                origAddr = addr + size;
                addr = origAddr;
            } while (knownSize < guess);

            return knownSize;
        }

        public void PatchData(PatchTargetKind targetKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup) {
            throw new NotImplementedException();
        }

        public IMemoryAllocator MemoryAllocator { get; } = new QueryingPagedMemoryAllocator(new PageAllocator());

        private sealed class PageAllocator : QueryingMemoryPageAllocatorBase {
            private readonly uint pageSize;
            public override uint PageSize => pageSize;

            public PageAllocator() {
                pageSize = (uint)Interop.OSX.GetPageSize();
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
