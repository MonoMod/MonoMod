using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class AndroidSystem : ISystem, IInitialize<IArchitecture>
    {
        public OSKind Target => OSKind.Android;

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

        public AndroidSystem()
        {
            PageSize = (nint)Android.Sysconf(Android.SysconfName.PageSize);
            allocator = new MmapPagedMemoryAllocator(PageSize);

            var dropPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ".cache");

            Directory.CreateDirectory(dropPath);
            Switches.SetSwitchValue(Switches.HelperDropPath, dropPath);

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

            // On Android, we should respect W^X since it is enforced in newer API levels.
            // However the runtime may not play nice so we just make it RWX for executable patches.
            // We can get away with it by targeting older API levels.
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
        }

        private void RoundToPageBoundary(ref nint addr, ref nint size)
        {
            var newAddr = allocator.RoundDownToPageBoundary(addr);
            size += addr - newAddr;
            addr = newAddr;
        }

        private void ProtectRWX(IntPtr addr, nint size)
        {
            RoundToPageBoundary(ref addr, ref size);
            if (Android.Mprotect(addr, (nuint)size, Android.Protection.Read | Android.Protection.Write | Android.Protection.Execute) != 0)
            {
                throw new Win32Exception(Android.Errno);
            }
        }

        private void ProtectRW(IntPtr addr, nint size)
        {
            RoundToPageBoundary(ref addr, ref size);
            if (Android.Mprotect(addr, (nuint)size, Android.Protection.Read | Android.Protection.Write) != 0)
            {
                throw new Win32Exception(Android.Errno);
            }
        }

        private void ProtectRX(IntPtr addr, nint size)
        {
            RoundToPageBoundary(ref addr, ref size);
            if (Android.Mprotect(addr, (nuint)size, Android.Protection.Read | Android.Protection.Execute) != 0)
            {
                throw new Win32Exception(Android.Errno);
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
                if (Android.Pipe2(pipefd, Android.PipeFlags.CloseOnExec) == -1)
                {
                    throw new Win32Exception(Android.Errno, "Failed to create pipe for page probes");
                }

                PageProbePipeReadFD = pipefd[0];
                PageProbePipeWriteFD = pipefd[1];
            }

            public static unsafe bool PageAllocated(nint page)
            {
                byte garbage;
                if (Android.Mincore(page, 1, &garbage) == -1)
                {
                    var lastError = Android.Errno;
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
                if (Android.Write(PageProbePipeWriteFD, page, 1) == -1)
                {
                    var lastError = Android.Errno;
                    if (lastError == 14)
                    {  // EFAULT, buf is not readable
                        return false;
                    }
                    throw new NotImplementedException($"Got unimplemented errno for write(2); errno = {lastError}");
                }

                // Success - clean up the pipe
                byte garbage;
                if (Android.Read(PageProbePipeReadFD, new IntPtr(&garbage), 1) == -1)
                {
                    throw new Win32Exception("Failed to clean up page probe pipe after successful page probe");
                }

                return true;
            }

            private bool canTestPageAllocation = true;

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
            {
                var prot = request.Executable ? Android.Protection.Execute : Android.Protection.None;
                prot |= Android.Protection.Read | Android.Protection.Write;

                // mmap the page we found
                var mmapPtr = Android.Mmap(IntPtr.Zero, (nuint)PageSize, prot, Android.MmapFlags.Private | Android.MmapFlags.Anonymous, -1, 0);
                if (mmapPtr is 0 or -1)
                {
                    // fuck
                    var errno = Android.Errno;
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

                var prot = request.Base.Executable ? Android.Protection.Execute : Android.Protection.None;
                prot |= Android.Protection.Read | Android.Protection.Write;

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
                var mmapPtr = Android.Mmap(ptr, (nuint)PageSize, prot, Android.MmapFlags.Private | Android.MmapFlags.Anonymous | Android.MmapFlags.FixedNoReplace, -1, 0);
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
                var res = Android.Munmap(page.BaseAddr, page.Size);
                if (res != 0)
                {
                    errorMsg = new Win32Exception(Android.Errno).Message;
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

        private static ReadOnlySpan<byte> NEHTempl => "/mm-exhelper.so.XXXXXX"u8;

        private sealed class AndroidNativeLibDrop : PosixNativeLibraryDrop
        {
            public static readonly AndroidNativeLibDrop Instance = new();

            protected override void CloseFileDescriptor(nint fd)
            {
                _ = Android.Close((int)fd);
            }

            protected override unsafe nint Mkstemp(Span<byte> template)
            {
                int fd;
                fixed (byte* pTmpl = template)
                    fd = Android.MkSTemp(pTmpl);

                if (fd == -1)
                {
                    var lastError = Android.Errno;
                    var ex = new Win32Exception(lastError);
                    MMDbgLog.Error($"Could not create temp file: {lastError} {ex}");
                    throw ex;
                }
                return fd;
            }
        }

        private unsafe PosixExceptionHelper CreateNativeExceptionHelper()
        {
            Helpers.Assert(arch is not null);

            var soname = arch.Target switch
            {
                ArchitectureKind.Arm64 => "exhelper_android_arm64.so",
                _ => throw new NotImplementedException($"No exception helper for current arch")
            };

            string fname;
            using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(soname))
            {
                Helpers.Assert(embedded is not null);
                fname = AndroidNativeLibDrop.Instance.DropLibrary(embedded, NEHTempl);
            }

            return PosixExceptionHelper.CreateHelper(arch, fname);
        }

        public unsafe IntPtr GetNativeJitHookConfig(int runtimeMajMin)
        {
            throw new NotImplementedException();
        }
    }
}
