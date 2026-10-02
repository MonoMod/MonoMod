using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class LinuxSystem : ISystem, IInitialize<IArchitecture>
    {
        public OSKind Target => OSKind.Linux;

        public SystemFeature Features => SystemFeature.RWXPages | SystemFeature.RXPages;

        private readonly Abi defaultAbi;
        public Abi? DefaultAbi => defaultAbi;

        public IEnumerable<LoadedModule> EnumerateLoadedModules()
        {
            foreach (var module in Linux.Procfs.ParseMapsModules(Linux.Procfs.ProcPid.Self)!)
            {
                yield return new LoadedModule(module.StartAddress, module.Path, module.Size);
            }
        }

        public IEnumerable<string?> EnumerateLoadedModuleFiles()
        {
            foreach (var module in EnumerateLoadedModules())
            {
                yield return module.FileName;
            }
        }

        private readonly nint PageSize;

        private readonly MmapPagedMemoryAllocator allocator;
        public IMemoryAllocator MemoryAllocator => allocator;

        public LinuxSystem()
        {
            PageSize = (nint)Unix.Sysconf(Unix.SysconfName.PageSize);
            allocator = new MmapPagedMemoryAllocator(PageSize);

            switch (PlatformDetection.Architecture)
            {
                case ArchitectureKind.x86_64:
                    defaultAbi = new Abi(
                        new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                        SystemVABI.ClassifyAMD64,
                        true
                    );
                    break;
                case ArchitectureKind.Arm64:
                    defaultAbi = new Abi(
                        new[]
                        {
                            //SpecialArgumentKind.ReturnBuffer, // ARM64 passes the return buffer in a dedicated register
                            SpecialArgumentKind.ThisPointer,
                            SpecialArgumentKind.UserArguments
                        },
                        SystemVABI.ClassifyARM64,
                        false
                    );
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public nint GetSizeOfReadableMemory(IntPtr start, nint guess)
        {
            var currentPage = allocator.RoundDownToPageBoundary(start);
            if (!MmapPagedMemoryAllocator.PageReadable(currentPage))
            {
                return 0;
            }
            currentPage += PageSize;

            var known = currentPage - start;

            while (known < guess)
            {
                if (!MmapPagedMemoryAllocator.PageReadable(currentPage))
                {
                    return known;
                }
                known += PageSize;
                currentPage += PageSize;
            }

            return known;
        }

        public unsafe void PatchData(PatchTargetKind patchKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup)
        {
            // TODO: should this be thread-safe? It definitely is not right now.

            // Update the protection of this
            if (patchKind == PatchTargetKind.Executable)
            {
                ProtectRWX(patchTarget, data.Length);
            }
            else
            {
                ProtectRW(patchTarget, data.Length);
            }

            var target = new Span<byte>((void*)patchTarget, data.Length);
            // now we copy target to backup, then data to target, then flush the instruction cache
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            if (patchKind is PatchTargetKind.Executable)
            {
                FlushInstructionCache(patchTarget, (nuint)target.Length);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe void FlushInstructionCache(IntPtr address, nuint size)
        {
            if (NativeExceptionHelper is ClearCacheExHelper cch)
            {
                cch.ClearCache((void*)address, size);
            }
            else
            {
                // not on a target arch that needs a clearcache helper, no-op
                // the one that's relevant for us is x86/x86-64, where a simple call/ret
                // is sufficient, generally. Thus, simply having gotten here is sufficient.
            }
        }

        private void RoundToPageBoundary(ref nint addr, ref nint size)
        {
            var newAddr = allocator.RoundDownToPageBoundary(addr);
            size += addr - newAddr;
            addr = newAddr;
        }

        private void ProtectRW(IntPtr addr, nint size)
        {
            RoundToPageBoundary(ref addr, ref size);
            if (Unix.Mprotect(addr, (nuint)size, Unix.Protection.Read | Unix.Protection.Write) != 0)
            {
                throw new Win32Exception(Unix.Errno);
            }
        }

        private void ProtectRWX(IntPtr addr, nint size)
        {
            RoundToPageBoundary(ref addr, ref size);
            if (Unix.Mprotect(addr, (nuint)size, Unix.Protection.Read | Unix.Protection.Write | Unix.Protection.Execute) != 0)
            {
                throw new Win32Exception(Unix.Errno);
            }
        }

        private sealed class MmapPagedMemoryAllocator : PagedMemoryAllocator
        {
            public MmapPagedMemoryAllocator(nint pageSize)
                : base(pageSize)
            {
            }

            [SuppressMessage("Design", "CA1032:Implement standard exception constructors")]
            [SuppressMessage("Design", "CA1064:Exceptions should be public",
                Justification = "This is used exclusively internally as jank control flow because I'm lazy")]
            private sealed class SyscallNotImplementedException : Exception { }

            private static int PageProbePipeReadFD, PageProbePipeWriteFD;

            [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations",
                Justification = "If the exception is thrown, the application is in an unrecoverable state. Methods on this type will not behave well.")]
            [SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline",
                Justification = "There is no good way to inline the initialization here, and we want to make sure that the cctor runs before anything is done with the type.")]
            static unsafe MmapPagedMemoryAllocator()
            {
                // Open a temporary pipe for page probes
                // This pipe gets leaked, but eh
                var pipefd = stackalloc int[2];
                if (Unix.Pipe2(pipefd, Unix.PipeFlags.CloseOnExec) == -1)
                {
                    throw new Win32Exception(Unix.Errno, "Failed to create pipe for page probes");
                }

                PageProbePipeReadFD = pipefd[0];
                PageProbePipeWriteFD = pipefd[1];
            }

            public static unsafe bool PageAllocated(nint page)
            {
                byte garbage;
                // TODO: Mincore isn't implemented in WSL, and always gives ENOSYS
                if (Unix.Mincore(page, 1, &garbage) == -1)
                {
                    var lastError = Unix.Errno;
                    if (lastError == 12)
                    {  // ENOMEM, page is unallocated
                        return false;
                    }
                    if (lastError == 38)
                    { // ENOSYS, function not implemented
                        // TODO: possibly implement /proc/self/maps parsing as a fallback
                        throw new SyscallNotImplementedException();
                    }
                    throw new NotImplementedException($"Got unimplemented errno for mincore(2); errno = {lastError}");
                }
                return true;
            }

            public static unsafe bool PageReadable(nint page)
            {
                // Try to write into a pipe using the page as the source buffer
                if (Unix.Write(PageProbePipeWriteFD, page, 1) == -1)
                {
                    var lastError = Unix.Errno;
                    if (lastError == 14)
                    {  // EFAULT, buf is not readable
                        return false;
                    }
                    throw new NotImplementedException($"Got unimplemented errno for write(2); errno = {lastError}");
                }

                // Success - clean up the pipe
                byte garbage;
                if (Unix.Read(PageProbePipeReadFD, new IntPtr(&garbage), 1) == -1)
                {
                    throw new Win32Exception("Failed to clean up page probe pipe after successful page probe");
                }

                return true;
            }

            private bool canTestPageAllocation = true;

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
            {
                var prot = request.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;

                // mmap the page we found
                var mmapPtr = Unix.Mmap(IntPtr.Zero, (nuint)PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous, -1, 0);
                if (mmapPtr is 0 or -1)
                {
                    // fuck
                    var errno = Unix.Errno;
                    MMDbgLog.Error($"Error creating allocation: {errno} {new Win32Exception(errno).Message}");
                    allocated = null;
                    return false;
                }

                // create a Page object for the newly mapped memory, even before deciding whether we succeeded or not
                var page = new Page(this, mmapPtr, (uint)PageSize, request.Executable);
                InsertAllocatedPage(page);

                // for simplicity, we'll try to allocate out of the page before checking bounds
                if (!page.TryAllocate((uint)request.Size, (uint)request.Alignment, out var pageAlloc))
                {
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
            )
            {
                if (!canTestPageAllocation)
                {
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

                try
                {
                    while (low >= lowPageBound || high <= highPageBound)
                    {

                        // check above the target page first
                        if (high <= highPageBound)
                        {
                            for (nint i = 0; i < numPages; i++)
                            {
                                if (PageAllocated(high + PageSize * i))
                                {
                                    high += PageSize;
                                    goto FailHigh;
                                }
                            }
                            // all pages are unallocated, we're done
                            ptr = high;
                            break;
                        }
                        FailHigh:
                        if (low >= lowPageBound)
                        {
                            for (nint i = 0; i < numPages; i++)
                            {
                                if (PageAllocated(low + PageSize * i))
                                {
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
                }
                catch (SyscallNotImplementedException)
                {
                    canTestPageAllocation = false;
                    allocated = null;
                    return false;
                }

                // unable to find a page within bounds
                if (ptr == -1)
                {
                    allocated = null;
                    return false;
                }

                // mmap the page we found
                var mmapPtr = Unix.Mmap(ptr, (nuint)PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous | Unix.MmapFlags.FixedNoReplace, -1, 0);
                if (mmapPtr is 0 or -1)
                {
                    // fuck
                    allocated = null;
                    return false;
                }

                // create a Page object for the newly mapped memory, even before deciding whether we succeeded or not
                var page = new Page(this, mmapPtr, (uint)PageSize, request.Base.Executable);
                InsertAllocatedPage(page);

                // for simplicity, we'll try to allocate out of the page before checking bounds
                if (!page.TryAllocate((uint)request.Base.Size, (uint)request.Base.Alignment, out var pageAlloc))
                {
                    // huh???
                    RegisterForCleanup(page);
                    allocated = null;
                    return false;
                }

                if ((nint)pageAlloc.BaseAddress < request.LowBound || (nint)pageAlloc.BaseAddress + pageAlloc.Size >= request.HighBound)
                {
                    // the allocation didn't land in bounds, fail out
                    pageAlloc.Dispose(); // because this is the only allocation in the page, this auto-registers it for cleanup
                    allocated = null;
                    return false;
                }

                // we got an allocation!
                allocated = pageAlloc;
                return true;
            }

            protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg)
            {
                var res = Unix.Munmap(page.BaseAddr, page.Size);
                if (res != 0)
                {
                    errorMsg = new Win32Exception(Unix.Errno).Message;
                    return false;
                }
                errorMsg = null;
                return true;
            }
        }

        private IArchitecture? arch;
        void IInitialize<IArchitecture>.Initialize(IArchitecture value)
        {
            arch = value;
        }

        private PosixExceptionHelper? lazyNativeExceptionHelper;
        public INativeExceptionHelper? NativeExceptionHelper => lazyNativeExceptionHelper ??= CreateNativeExceptionHelper();

        private static ReadOnlySpan<byte> NEHTempl => "/tmp/mm-exhelper.so.XXXXXX"u8;

        private sealed class LinuxNativeLibDrop : PosixNativeLibraryDrop
        {
            public static readonly LinuxNativeLibDrop Instance = new();

            protected override void CloseFileDescriptor(nint fd)
            {
                _ = Unix.Close((int)fd);
            }

            protected override unsafe nint Mkstemp(Span<byte> template)
            {
                int fd;
                fixed (byte* pTmpl = template)
                    fd = Unix.MkSTemp(pTmpl);

                if (fd == -1)
                {
                    var lastError = Unix.Errno;
                    var ex = new Win32Exception(lastError);
                    MMDbgLog.Error($"Could not create temp file: {lastError} {ex}");
                    throw ex;
                }
                return fd;
            }
        }

        private PosixExceptionHelper CreateNativeExceptionHelper()
        {
            Helpers.Assert(arch is not null);

            var soname = arch.Target switch
            {
                ArchitectureKind.x86_64 => "exhelper_linux_x86_64.so",
                ArchitectureKind.Arm64 => "exhelper_linux_arm64.so",
                _ => throw new NotImplementedException($"No exception helper for current arch")
            };

            string fname;
            using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(soname))
            {
                Helpers.Assert(embedded is not null);
                fname = LinuxNativeLibDrop.Instance.DropLibrary(embedded, NEHTempl);
            }

            if (arch.Target is ArchitectureKind.Arm64)
            {
                // have extra clear cache helper we need
                return ClearCacheExHelper.CreateHelper(arch, fname);
            }

            return PosixExceptionHelper.CreateHelper(arch, fname);
        }

        private sealed class ClearCacheExHelper : PosixExceptionHelper
        {
            private readonly IntPtr clearCache;

            public ClearCacheExHelper(IArchitecture arch, IntPtr getExPtr, IntPtr m2n, IntPtr n2m, IntPtr clearCache) : base(arch, getExPtr, m2n, n2m)
            {
                this.clearCache = clearCache;
            }

            public static new ClearCacheExHelper CreateHelper(IArchitecture arch, string filename, bool deleteAfterLoad = true)
            {
                // we've now got the file on disk, and we know its name. lets load it
                var handle = DynDll.OpenLibrary(filename);
                IntPtr eh_get_exception_ptr, eh_managed_to_native, eh_native_to_managed, mmh_clear_cache;
                try
                {
                    // once the library's been opened, we can delete it
                    if (deleteAfterLoad)
                    {
                        // note: File.Delete() forwards to `unlink(2)`, which removes the name but lets
                        // existing fds (such as for the mapping we used to load the file) stay around.
                        System.IO.File.Delete(filename);
                    }

                    eh_get_exception_ptr = DynDll.GetExport(handle, nameof(eh_get_exception_ptr));
                    eh_managed_to_native = DynDll.GetExport(handle, nameof(eh_managed_to_native));
                    eh_native_to_managed = DynDll.GetExport(handle, nameof(eh_native_to_managed));
                    mmh_clear_cache = DynDll.GetExport(handle, nameof(mmh_clear_cache));

                    Helpers.Assert(eh_get_exception_ptr != IntPtr.Zero);
                    Helpers.Assert(eh_managed_to_native != IntPtr.Zero);
                    Helpers.Assert(eh_native_to_managed != IntPtr.Zero);
                    Helpers.Assert(eh_native_to_managed != IntPtr.Zero);
                    Helpers.Assert(mmh_clear_cache != IntPtr.Zero);
                }
                catch
                {
                    DynDll.CloseLibrary(handle);
                    throw;
                }

                return new ClearCacheExHelper(arch, eh_get_exception_ptr, eh_managed_to_native, eh_native_to_managed, mmh_clear_cache);
            }

            public unsafe void ClearCache(void* addr, nuint size)
            {
                ((delegate* unmanaged[Cdecl]<void*, nuint, void>)clearCache)(addr, size);
            }
        }

        public unsafe IntPtr GetNativeJitHookConfig(int runtimeMajMin)
        {
            throw new NotImplementedException();
        }
    }
}
