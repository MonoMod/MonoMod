using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms.Systems {
    internal class LinuxSystem : ISystem {
        public OSKind Target => OSKind.Linux;

        public SystemFeature Features => SystemFeature.RWXPages | SystemFeature.RXPages;

        public Abi? DefaultAbi => null;

        public IMemoryAllocator MemoryAllocator { get; } = new MmapPagedMemoryAllocator();

        public nint GetSizeOfReadableMemory(IntPtr start, nint guess) {
            // don't currently have a good way to do this, so sucks
            throw new NotImplementedException();
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

        private static void ProtectRW(IntPtr addr, nint size) {
            if (Interop.Unix.Mprotect(addr, (nuint) size, Interop.Unix.Protection.Read | Interop.Unix.Protection.Write) != 0) {
                throw new Win32Exception();
            }
        }

        private static void ProtectRWX(IntPtr addr, nint size) {
            if (Interop.Unix.Mprotect(addr, (nuint) size, Interop.Unix.Protection.Read | Interop.Unix.Protection.Write | Interop.Unix.Protection.Execute) != 0) {
                throw new Win32Exception();
            }
        }

        private sealed class MmapPagedMemoryAllocator : PagedMemoryAllocator {
            public MmapPagedMemoryAllocator()
                : base((nint) Interop.Unix.Sysconf(Interop.Unix.SysconfName.PageSize)) {
            }

            protected override bool TryAllocateNewPage(
                AllocationRequest request,
                nint targetPage, nint lowPageBound, nint highPageBound,
                [MaybeNullWhen(false)] out IAllocatedMemory allocated
            ) {
                var prot = request.Executable ? Interop.Unix.Protection.Execute : Interop.Unix.Protection.None;
                prot |= Interop.Unix.Protection.Read | Interop.Unix.Protection.Write;

                // we'll just try an mmap
                var ptr = Interop.Unix.Mmap(targetPage, (nuint) PageSize, prot, Interop.Unix.MmapFlags.Anonymous, -1, 0);
                if ((nint) ptr == -1) {
                    // mmap failed, uh oh
                    allocated = null;
                    return false;
                }

                // create a Page object for the newly mapped memory, even before deciding whether we succeeded or not
                var page = new Page(this, ptr, (uint) PageSize, request.Executable);
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
                var res = Interop.Unix.Munmap(page.BaseAddr, page.Size);
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
