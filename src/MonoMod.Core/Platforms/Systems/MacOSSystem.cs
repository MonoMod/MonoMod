using MonoMod.Core.Platforms.Memory;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms.Systems {
    internal class MacOSSystem : ISystem {
        public OSKind Target => OSKind.OSX;

        public SystemFeature Features => SystemFeature.RXPages | SystemFeature.RWXPages;

        public Abi? DefaultAbi => default; // TODO:

        public INativeExceptionHelper? NativeExceptionHelper => throw new NotImplementedException();

        public unsafe nint GetSizeOfReadableMemory(IntPtr start, nint guess) {
            nint knownSize = 0;

            var selfTask = Interop.OSX.mach_task_self();
            var origAddr = (ulong) start;
            var addr = origAddr;
            do {
                var infoSize = sizeof(Interop.OSX.vm_region_basic_info_64);
                if (!Interop.OSX.mach_vm_region(selfTask, ref addr, out var size, Interop.OSX.vm_region_flavor_t.BasicInfo64, out var info, ref infoSize, out _)) {
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
