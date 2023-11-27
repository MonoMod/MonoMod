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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core.Platforms.Systems {
    internal sealed class LinuxSystem : ISystem, IInitialize<IArchitecture> {
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
            if (!MmapPagedMemoryAllocator.PageAllocated(currentPage, checkReadable: true)) {
                return 0;
            }
            currentPage += PageSize;
            
            var known = currentPage - start;

            while (known < guess) {
                if (!MmapPagedMemoryAllocator.PageAllocated(currentPage, checkReadable: true)) {
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
                throw new Win32Exception(Unix.Errno);
            }
        }

        private void ProtectRWX(IntPtr addr, nint size) {
            RoundToPageBoundary(ref addr, ref size);
            if (Unix.Mprotect(addr, (nuint) size, Unix.Protection.Read | Unix.Protection.Write | Unix.Protection.Execute) != 0) {
                throw new Win32Exception(Unix.Errno);
            }
        }

        private sealed class MmapPagedMemoryAllocator : PagedMemoryAllocator {
            public MmapPagedMemoryAllocator(nint pageSize)
                : base(pageSize) {
            }

            public static unsafe bool PageAllocated(nint page, bool checkReadable = false) {
                byte garbage;

                // First check if the page is allocated through the mincore syscall
                var didMincoreCheck = true;
                if (Unix.Mincore(page, 1, &garbage) == -1) {
                    var lastError = Unix.Errno;
                    if (lastError == 12) {  // ENOMEM, page is unallocated
                        return false;
                    } else if (lastError == 38) { // ENOSYS Function not implemented
                        didMincoreCheck = false;
                    } else {
                        throw new NotImplementedException($"Got unimplemented errno for mincore(2); errno = {lastError}");
                    }
                }

                // Parse /proc/self/maps and check if the memory region is readable
                // Also parse when the mincore syscall is not available
                if (checkReadable || !didMincoreCheck) {
                    using StreamReader reader = new StreamReader("/proc/self/maps");
                    while (reader.ReadLine() is string line) {
                        var lineSpan = line.AsSpan();

                        // Parse the range
                        var addrSeparatorIndex = line.IndexOf('-', StringComparison.InvariantCulture);
                        var permsSeparatorIndex = line.IndexOf(' ', StringComparison.InvariantCulture);

                        var startAddr = nuint.Parse(lineSpan[..addrSeparatorIndex], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        var endAddr = nuint.Parse(lineSpan[(addrSeparatorIndex+1)..permsSeparatorIndex], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        var perms = lineSpan[(permsSeparatorIndex+1)..(permsSeparatorIndex+5)];

                        // Check if the range contains the target page
                        if (unchecked((nuint) page) < startAddr) {
                            // We skipped over the target page, so no range contains it
                            return false;
                        }
                        if (endAddr <= unchecked((nuint) page)) {
                            continue;
                        }

                        // Check the permissions
                        return !checkReadable || perms[0] == 'r';
                    }

                    // No range contained the target page
                    return false;
                }

                return true;
            }

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                var prot = request.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;

                // mmap the page we found
                var mmapPtr = Unix.Mmap(IntPtr.Zero, (nuint) PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous, -1, 0);
                if (mmapPtr is 0 or -1) {
                    // fuck
                    var errno = Unix.Errno;
                    MMDbgLog.Error($"Error creating allocation: {errno} {new Win32Exception(errno).Message}");
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
                var prot = request.Base.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;
                
                // number of pages needed to satisfy length requirements
                var numPages = request.Base.Size / PageSize + 1;
                
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
                    errorMsg = new Win32Exception(Unix.Errno).Message;
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
                templ.AsSpan().Clear();
                NEHTempl.CopyTo(templ);

                fixed (byte* pTmpl = templ)
                    fd = Unix.MkSTemp(pTmpl);

                if (fd == -1) {
                    var lastError = Unix.Errno;
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
                using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(soname);
                Helpers.Assert(embedded is not null);

                embedded.CopyTo(fs);
            }
            return PosixExceptionHelper.CreateHelper(arch, fname);
        }
    }
}
