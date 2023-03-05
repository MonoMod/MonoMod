using Microsoft.Win32.SafeHandles;
using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.Core.Platforms.Systems {
    internal class LinuxSystem : ISystem, IInitialize<IArchitecture> {
        public OSKind Target => OSKind.Linux;

        public SystemFeature Features => SystemFeature.RWXPages | SystemFeature.RXPages;

        private readonly Abi defaultAbi;
        public Abi? DefaultAbi => defaultAbi;

        public IEnumerable<string?> EnumerateLoadedModuleFiles() {
            return Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m => m.FileName)!;
        }

        private readonly nint PageSize;

        private readonly MmapPagedMemoryAllocator allocator;
        public IMemoryAllocator MemoryAllocator => allocator;

        public LinuxSystem() {
            PageSize = (nint)Unix.Sysconf(Unix.SysconfName.PageSize);
            allocator = new MmapPagedMemoryAllocator(PageSize);

            if (PlatformDetection.Architecture == ArchitectureKind.x86_64) {
                defaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    SystemVABI.ClassifyAMD64,
                    true
                );
            } else {
                throw new NotImplementedException();
            }
        }

        public nint GetSizeOfReadableMemory(IntPtr start, nint guess) {
            var currentPage = allocator.RoundDownToPageBoundary(start);
            if (!MmapPagedMemoryAllocator.PageAllocated(currentPage)) {
                return 0;
            }
            currentPage += PageSize;
            
            var known = currentPage - start;

            while (known < guess) {
                if (!MmapPagedMemoryAllocator.PageAllocated(currentPage)) {
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

            public static unsafe bool PageAllocated(nint page) {
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

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                var prot = request.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;

                // mmap the page we found
                var mmapPtr = Unix.Mmap(IntPtr.Zero, (nuint) PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous, -1, 0);
                if (mmapPtr is 0 or -1) {
                    // fuck
                    int errno;
                    MMDbgLog.Error($"Error creating allocation: {errno = MarshalEx.GetLastPInvokeError()} {new Win32Exception(errno).Message}");
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

                // we got an allocation!
                allocated = pageAlloc;
                return true;
            }

            protected override bool TryAllocateNewPage(
                PositionedAllocationRequest request,
                nint targetPage, nint lowPageBound, nint highPageBound,
                [MaybeNullWhen(false)] out IAllocatedMemory allocated
            ) {
                if (!canTestPageAllocation) {
                    allocated = null;
                    return false;
                }

                var prot = request.Base.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;
                
                // number of pages needed to satisfy length requirements
                var numPages = request.Base.Size / PageSize + 1;
                
                // find the nearest unallocated page within our bounds
                var low = targetPage - PageSize;
                var high = targetPage;
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
                var mmapPtr = Unix.Mmap(ptr, (nuint) PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous | Unix.MmapFlags.FixedNoReplace, -1, 0);
                if (mmapPtr is 0 or -1) {
                    // fuck
                    allocated = null;
                    return false;
                }

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
                var res = Unix.Munmap(page.BaseAddr, page.Size);
                if (res != 0) {
                    errorMsg = new Win32Exception().Message;
                    return false;
                }
                errorMsg = null;
                return true;
            }
        }

        private IArchitecture? arch;
        void IInitialize<IArchitecture>.Initialize(IArchitecture value) {
            arch = value;
        }

        private PosixExceptionHelper? lazyNativeExceptionHelper;
        public INativeExceptionHelper? NativeExceptionHelper => lazyNativeExceptionHelper ??= CreateNativeExceptionHelper();

        private static ReadOnlySpan<byte> NEHTempl => "/tmp/mm-exhelper.so.XXXXXX"u8;

        private unsafe PosixExceptionHelper CreateNativeExceptionHelper() {
            Helpers.Assert(arch is not null);

            var soname = arch.Target switch {
                ArchitectureKind.x86_64 => "exhelper_linux_x86_64.so",
                _ => throw new NotImplementedException($"No exception helper for current arch")
            };

            // we want to get a temp file, write our helper to it, and load it
            var templ = ArrayPool<byte>.Shared.Rent(NEHTempl.Length + 1);
            int fd;
            string fname;
            try {
                templ.AsSpan().Fill(0);
                NEHTempl.CopyTo(templ);

                fixed (byte* pTmpl = templ)
                    fd = Unix.MkSTemp(pTmpl);

                if (fd == -1) {
                    var lastError = Marshal.GetLastWin32Error();
                    var ex = new Win32Exception(lastError);
                    MMDbgLog.Error($"Could not create temp file for NativeExceptionHelper: {lastError} {ex}");
                    throw ex;
                }

                fname = Encoding.UTF8.GetString(templ, 0, NEHTempl.Length);
            } finally {
                ArrayPool<byte>.Shared.Return(templ);
            }

            using (var fh = new SafeFileHandle((IntPtr) fd, true))
            using (var fs = new FileStream(fh, FileAccess.Write)) {
                return PosixExceptionHelper.CreateHelper(arch, soname, fname, fs);
            }
        }
    }
}
