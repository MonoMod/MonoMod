using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using static MonoMod.Core.Interop.OSX;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class MacOSSystem : ISystem, IInitialize<IArchitecture>
    {
        public OSKind Target => OSKind.OSX;

        public SystemFeature Features { get; }

        public Abi? DefaultAbi { get; }

        public uint PageSize { get; } = (uint)GetPageSize();

        public MacOSSystem()
        {
            switch (PlatformDetection.Architecture)
            {
                case ArchitectureKind.x86_64:
                    // As best I can find (Apple docs are worthless) MacOS uses SystemV on x64
                    Features = SystemFeature.RXPages | SystemFeature.RWXPages;
                    DefaultAbi = new Abi(
                        new[]
                        {
                            SpecialArgumentKind.ReturnBuffer,
                            SpecialArgumentKind.ThisPointer,
                            SpecialArgumentKind.UserArguments
                        },
                        SystemVABI.ClassifyAMD64,
                        true
                    );
                    break;
                case ArchitectureKind.Arm64:
                    Features = SystemFeature.RXPages | SystemFeature.MayUseNativeJitHooks;
                    DefaultAbi = new Abi(
                        new[]
                        {
                            //SpecialArgumentKind.ReturnBuffer, // Arm64 uses a dedicated register for return buffers
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

        public unsafe IEnumerable<LoadedModule> EnumerateLoadedModules()
        {
            var infoCnt = task_dyld_info.Count;
            var dyldInfo = default(task_dyld_info);
            var kr = task_info(mach_task_self(), task_flavor_t.DyldInfo, &dyldInfo, &infoCnt);
            if (!kr)
            {
                return []; // could not get own dyld info
            }

            var infos = dyldInfo.all_image_infos->InfoArray;

            var arr = new LoadedModule[infos.Length];
            for (var i = 0; i < arr.Length; i++)
            {
                var info = infos[i];
                // TODO get size, probably by parsing the mach header
                arr[i] = new LoadedModule((ulong)info.imageLoadAddress, info.imageFilePath.ToString(), null);
            }

            return arr;
        }

        public IEnumerable<string?> EnumerateLoadedModuleFiles()
        {
            foreach (var module in EnumerateLoadedModules())
            {
                yield return module.FileName;
            }
        }

        public unsafe nint GetSizeOfReadableMemory(IntPtr start, nint guess)
        {
            nint knownSize = 0;

            do
            {
                if (!GetLocalRegionInfo(start, out var realStart, out var realSize, out var prot, out _))
                {
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

        public unsafe void PatchData(PatchTargetKind targetKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup)
        {

            // targetKind is a hint for what the caller believes the memory to be. Because MacOS is more strict than Linux or Windows,
            // we need to actually check that to behave correctly in all cases.

            //var selfTask = mach_task_self();
            var len = data.Length;

            //bool memIsRead;
            bool memIsWrite;
            bool memIsExec;

            // we assume these defaults; this may end up blowing up completely
            //var canMakeRead = true;
            //var canMakeWrite = true;
            //var canMakeExec = true;

            if (TryGetProtForMem(patchTarget, len, out _/*var maxProt*/, out var curProt, out var crossesBoundary, out var notAllocated))
            {
                if (crossesBoundary)
                {
                    MMDbgLog.Warning($"Patch requested for memory which spans multiple memory allocations. Failures may result. (0x{patchTarget:x16} length {len})");
                }

                //memIsRead = curProt.Has(vm_prot_t.Read);
                memIsWrite = curProt.Has(vm_prot_t.Write);
                memIsExec = curProt.Has(vm_prot_t.Execute);
                //canMakeRead = maxProt.Has(vm_prot_t.Read);
                //canMakeWrite = maxProt.Has(vm_prot_t.Write);
                //canMakeExec = maxProt.Has(vm_prot_t.Execute);
            }
            else
            {
                // we couldn't get prot info
                // was it because the region wasn't allocated (in part or in full)?
                if (notAllocated)
                {
                    MMDbgLog.Error($"Requested patch of region which was not fully allocated (0x{patchTarget:x16} length {len})");
                    throw new InvalidOperationException("Cannot patch unallocated region"); // TODO: is there a better exception for this?
                }
                // otherwise, assume based on what the caller gave us
                //memIsRead = true;
                memIsWrite = false;
                memIsExec = targetKind is PatchTargetKind.Executable;
            }

            // We know know what protections the target memory region has, so we can decide on a course of action.

            if (!memIsWrite)
            {
                Helpers.Assert(!crossesBoundary);
                MakePageWritable((nuint)(nint)patchTarget, (nuint)data.Length);
                curProt = vm_prot_t.All;
            }

            // at this point, we know our data to be writable

            // now we copy target to backup, then data to target
            var target = new Span<byte>((void*)patchTarget, data.Length);
            _ = target.TryCopyTo(backup);
            
            if (NativeExceptionHelper is JitMemcpyHelper gcmh && curProt == vm_prot_t.All)
            {
                MMDbgLog.Trace($"RWX memory detected, doing memcpy for MAP_JIT");
                
                fixed (byte* dataPtr = data)
                {
                    gcmh.JitMemCpy(patchTarget, (IntPtr)dataPtr, (ulong)data.Length);
                    MMDbgLog.Trace($"{data.Length} bytes written to 0x{patchTarget:X16}");
                }
            }
            else
            {
                data.CopyTo(target);
            }

            // if we got here when executable (either because the memory was already writable or we were able to make it writable) we need to flush the icache
            if (memIsExec)
            {
                sys_icache_invalidate((void*)patchTarget, (nuint)data.Length);
            }
        }

        private unsafe void MakePageWritable(nuint addrInPage, nuint bufferSize)
        {
            var pageAddress = addrInPage & ~((nuint)PageSize - 1);
            if (addrInPage + bufferSize > pageAddress + PageSize) {
                MMDbgLog.Warning("Crossed pages while performing page remap");
                MakePageWritable(pageAddress, PageSize);
                MakePageWritable(pageAddress + PageSize, bufferSize - PageSize + (addrInPage - pageAddress));
                return;
            }

            Helpers.Assert(GetLocalRegionInfo((nint)pageAddress, out _, out _, out var allocProt, out var allocMaxProt));

            if (allocProt.Has(vm_prot_t.Write))
                return;

            kern_return_t kr;

            var selfTask = mach_task_self();

            if (allocMaxProt.Has(vm_prot_t.Write) || PlatformDetection.Architecture != ArchitectureKind.Arm64)
            {
                kr = mach_vm_protect(selfTask, pageAddress, PageSize, false, allocProt | vm_prot_t.Write);
                if (!kr)
                {
                    MMDbgLog.Error($"Could not vm_protect page 0x{pageAddress:x16}+0x{PageSize:x} " +
                        $"from {P(allocProt)} to {P(allocProt | vm_prot_t.Write)} (max prot {P(allocMaxProt)}): kr = {kr.Value}");
                    MMDbgLog.Error("Trying copy/remap instead...");
                    // fall out to try page remap
                }
                else
                {
                    // succeeded, bail out
                    return;
                }
            }

            // make sure we can read the page in the first place
            if (!allocProt.Has(vm_prot_t.Read))
            {
                if (!allocMaxProt.Has(vm_prot_t.Read))
                {
                    // max prot doesn't have read, can't continue
                    MMDbgLog.Error($"Requested 0x{pageAddress:x16}+0x{PageSize:x} (max: {P(allocMaxProt)}) to be made writable, but its not readable!");
                    throw new NotSupportedException("Cannot make page writable because its not readable");
                }
                kr = mach_vm_protect(selfTask, pageAddress, PageSize, false, allocProt | vm_prot_t.Read);
                if (!kr)
                {
                    MMDbgLog.Error($"vm_protect of 0x{pageAddress:x16}+0x{PageSize:x} (max: {P(allocMaxProt)}) to become readable failed: kr = {kr.Value}");
                    throw new NotSupportedException("Could not make page readable for remap");
                }
            }

            MMDbgLog.Trace($"Performing page remap on 0x{pageAddress:x16}+0x{PageSize:x} from {P(allocProt)}/{P(allocMaxProt)} to {P(vm_prot_t.All)}");

            // first, alloc a new page
            var newAddr = mmap(IntPtr.Zero, PageSize, map_prot.Read | map_prot.Write | map_prot.Execute, map_flags.Anonymous | map_flags.Private | map_flags.JIT, -1, 0);
            if (newAddr == MAP_FAILED)
            {
                throw new Win32Exception(Errno);
            }

            try
            {
                // then copy data from the map into it
                if (NativeExceptionHelper is JitMemcpyHelper gcmh)
                {
                    gcmh.JitMemCpy(newAddr, (nint)pageAddress, PageSize);
                }
                else
                {
                    new Span<byte>((void*)pageAddress, (int)PageSize).CopyTo(new Span<byte>((void*)newAddr, (int)PageSize));
                }

                // then map it over the old memory segment
                var targetAddr = (ulong)pageAddress;
                vm_prot_t curProt, maxProt;
                kr = mach_vm_remap(selfTask, &targetAddr, PageSize, 0, vm_flags.Fixed | vm_flags.Overwrite, selfTask, (ulong)newAddr, true, &curProt, &maxProt, vm_inherit_t.Copy);
                if (!kr)
                {
                    MMDbgLog.Error($"vm_remap() failed to map over target range: 0x{targetAddr:x16}+0x{PageSize:x} ({P(allocProt)}/{P(allocMaxProt)})" +
                                   $" <- 0x{newAddr:x16}+0x{PageSize:x} ({P(vm_prot_t.All)}/{P(vm_prot_t.All)}), kr = {kr.Value}");
                    throw new NotSupportedException("vm_map() failed");
                }
            }
            finally
            {
                // then unmap the created memory
                kr = mach_vm_deallocate(selfTask, (ulong)newAddr, PageSize);
                if (!kr)
                {
                    MMDbgLog.Error($"Could not deallocate created memory page 0x{newAddr:x16}+0x{PageSize:x}! kr = {kr.Value}");
                }
            }
        }

        private static unsafe bool TryGetProtForMem(nint addr, int length, out vm_prot_t maxProt, out vm_prot_t prot, out bool crossesAllocBoundary, out bool notAllocated)
        {
            maxProt = (vm_prot_t)(-1);
            prot = (vm_prot_t)(-1);

            crossesAllocBoundary = false;
            notAllocated = false;

            var origAddr = addr;

            do
            {
                if (addr >= origAddr + length)
                    break;

                // TODO: use mach_vm_region_recurse directly to enumerate consecutive regions sanely
                var kr = GetLocalRegionInfo(addr, out var startAddr, out var realSize, out var iprot, out var iMaxProt);
                if (kr)
                {
                    if (startAddr > addr)
                    {
                        // the address isn't allocated, and it returned the next region
                        notAllocated = true;
                        return false;
                    }

                    // if our region crosses alloc boundaries, we return the union of all prots
                    prot &= iprot;
                    maxProt &= iMaxProt;

                    addr = startAddr + realSize;

                    if (addr < origAddr + length)
                    {
                        // the end of this alloc is before the end of the requrested region, so we cross a boundary
                        crossesAllocBoundary = true;
                        continue;
                    }
                }
                else
                {
                    if (kr == kern_return_t.NoSpace)
                    {
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
        private static unsafe kern_return_t GetLocalRegionInfo(nint origAddr, out nint startAddr, out nint outSize, out vm_prot_t prot, out vm_prot_t maxProt)
        {
            kern_return_t kr;
            ulong size;
            var depth = int.MaxValue;

            vm_region_submap_short_info_64 info;
            var count = vm_region_submap_short_info_64.Count;
            var addr = (ulong)origAddr;
            kr = mach_vm_region_recurse(mach_task_self(), &addr, &size, &depth, &info, &count);
            if (!kr)
            {
                startAddr = default;
                outSize = default;
                prot = default;
                maxProt = default;
                return kr;
            }

            Helpers.Assert(!info.is_submap);
            startAddr = (nint)addr;
            outSize = (nint)size;
            prot = info.protection;
            maxProt = info.max_protection;
            return kr;
        }

        public IMemoryAllocator MemoryAllocator { get; } = new QueryingPagedMemoryAllocator(new MacOsQueryingAllocator());

        private sealed class MacOsQueryingAllocator : QueryingMemoryPageAllocatorBase
        {
            public override uint PageSize { get; }

            public MacOsQueryingAllocator()
            {
                PageSize = (uint)GetPageSize();
            }

            public override unsafe bool TryAllocatePage(nint size, bool executable, out IntPtr allocated)
            {
                Helpers.Assert(size == PageSize);

                var prot = executable ? vm_prot_t.Execute : vm_prot_t.None;
                prot |= vm_prot_t.Read | vm_prot_t.Write;

                if (PlatformDetection.Architecture == ArchitectureKind.Arm64 && prot == vm_prot_t.All)
                {
                    MMDbgLog.Trace($"RWX memory detected, doing mmap with MAP_JIT");

                    allocated = mmap(IntPtr.Zero, (ulong)size, map_prot.Read | map_prot.Write | map_prot.Execute, map_flags.Private | map_flags.Anonymous | map_flags.JIT, -1, 0);
                    if (allocated == (IntPtr)(-1))
                    {
                        var lastError = Errno;
                        var ex = new Win32Exception(lastError);
                        MMDbgLog.Error($"Error creating allocation anywhere! {lastError} {ex}");
                        allocated = default;
                        
                        return false;
                    }
                    
                    MMDbgLog.Trace($"RWX memory allocated to 0x{allocated:X16} with size {size}");

                    return true;
                }
                
                // map the page
                var addr = 0uL;
                var kr = mach_vm_map(mach_task_self(), &addr, (ulong)size, 0, vm_flags.Anywhere,
                    0, 0, true, prot, prot, vm_inherit_t.Default);
                if (!kr)
                {
                    MMDbgLog.Error($"Error creating allocation anywhere! kr = {kr.Value}");
                    allocated = default;
                    return false;
                }

                allocated = (IntPtr)addr;
                return true;
            }

            public override unsafe bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated)
            {
                Helpers.Assert(size == PageSize);

                var prot = executable ? vm_prot_t.Execute : vm_prot_t.None;
                prot |= vm_prot_t.Read | vm_prot_t.Write;
                
                if (PlatformDetection.Architecture == ArchitectureKind.Arm64 && prot == vm_prot_t.All)
                {
                    MMDbgLog.Trace($"RWX memory detected, doing mmap with MAP_JIT");

                    allocated = mmap(pageAddr, (ulong)size, map_prot.Read | map_prot.Write | map_prot.Execute, map_flags.Fixed | map_flags.Private | map_flags.Anonymous | map_flags.JIT, -1, 0);
                    if (allocated == (IntPtr)(-1))
                    {
                        var lastError = Errno;
                        var ex = new Win32Exception(lastError);
                        MMDbgLog.Error($"Error creating allocation anywhere! {lastError} {ex}");
                        allocated = default;
                        
                        return false;
                    }
                    
                    MMDbgLog.Trace($"RWX memory allocated to page at 0x{pageAddr:X16} with size {size}");
                    
                    return true;
                }

                // map the page
                var addr = (ulong)pageAddr;
                var kr = mach_vm_map(mach_task_self(), &addr, (ulong)size, 0, vm_flags.Fixed,
                    0, 0, true, prot, prot, vm_inherit_t.Default);
                if (!kr)
                {
                    MMDbgLog.Spam($"Error creating allocation at 0x{addr:x16}: kr = {kr.Value}");
                    allocated = default;
                    return false;
                }

                allocated = (IntPtr)addr;
                return true;
            }

            public override bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg)
            {
                var kr = mach_vm_deallocate(mach_task_self(), (ulong)pageAddr, PageSize);
                if (!kr)
                {
                    errorMsg = $"Could not deallocate page: kr = {kr.Value}";
                    return false;
                }
                errorMsg = null;
                return true;
            }

            public override bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize)
            {
                var kr = GetLocalRegionInfo(pageAddr, out allocBase, out allocSize, out _, out _);
                if (kr)
                {
                    if (allocBase > (nint)pageAddr)
                    {
                        allocSize = allocBase - (nint)pageAddr;
                        allocBase = pageAddr;
                        isFree = true;
                        return true;
                    }
                    else
                    {
                        isFree = false;
                        return true;
                    }
                }
                else if (kr == kern_return_t.InvalidAddress)
                {
                    isFree = true;
                    return true;
                }
                else
                {
                    isFree = false;
                    return false;
                }
            }
        }

        private IArchitecture? arch;
        void IInitialize<IArchitecture>.Initialize(IArchitecture value)
        {
            arch = value;
        }

        private PosixExceptionHelper? lazyNativeExceptionHelper;
        public INativeExceptionHelper? NativeExceptionHelper => lazyNativeExceptionHelper ??= CreateNativeExceptionHelper();

        public unsafe IntPtr GetNativeJitHookConfig(int runtimeMajMin)
        {
            if (NativeExceptionHelper is JitMemcpyHelper gcmh)
            {
                return gcmh.GetJitHookConfig(runtimeMajMin);
            }

            return IntPtr.Zero;
        }

        private static ReadOnlySpan<byte> NEHTempl => "/tmp/mm-exhelper.dylib.XXXXXX"u8;

        private sealed class MacOSNativeLibDrop : PosixNativeLibraryDrop
        {
            public static readonly MacOSNativeLibDrop Instance = new();

            protected override void CloseFileDescriptor(nint fd)
            {
                _ = OSX.Close((int)fd);
            }

            protected override unsafe nint Mkstemp(Span<byte> template)
            {
                int fd;
                fixed (byte* pTmpl = template)
                    fd = MkSTemp(pTmpl);

                if (fd == -1)
                {
                    var lastError = OSX.Errno;
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
                ArchitectureKind.x86_64 => "exhelper_macos_x86_64.dylib",
                ArchitectureKind.Arm64 => "exhelper_macos_arm64.dylib",
                _ => throw new NotImplementedException($"No exception helper for current arch")
            };

            string fname;
            using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(soname))
            {
                Helpers.Assert(embedded is not null);
                fname = MacOSNativeLibDrop.Instance.DropLibrary(embedded, NEHTempl);
            }

            return arch.Target is ArchitectureKind.Arm64
                ? JitMemcpyHelper.CreateHelper(arch, fname)
                : PosixExceptionHelper.CreateHelper(arch, fname);
        }

        private sealed class JitMemcpyHelper : PosixExceptionHelper
        {
            private readonly IntPtr mmch_jit_memcpy;
            private readonly IntPtr mmch_jit_hook_config;

            private JitMemcpyHelper(IArchitecture arch, IntPtr getExPtr, IntPtr m2n, IntPtr n2m, IntPtr memcpy, IntPtr jitCfg)
                : base(arch, getExPtr, m2n, n2m)
            {
                mmch_jit_memcpy = memcpy;
                mmch_jit_hook_config = jitCfg;
            }

            public static new JitMemcpyHelper CreateHelper(IArchitecture arch, string filename, bool deleteAfterLoad = true)
            {
                // we've now got the file on disk, and we know its name. lets load it
                var handle = DynDll.OpenLibrary(filename);
                IntPtr eh_get_exception_ptr, eh_managed_to_native, eh_native_to_managed, mmch_jit_memcpy, mmch_jit_hook_config;
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
                    mmch_jit_memcpy = DynDll.GetExport(handle, nameof(mmch_jit_memcpy));
                    mmch_jit_hook_config = DynDll.GetExport(handle, nameof(mmch_jit_hook_config));

                    Helpers.Assert(eh_get_exception_ptr != IntPtr.Zero);
                    Helpers.Assert(eh_managed_to_native != IntPtr.Zero);
                    Helpers.Assert(eh_native_to_managed != IntPtr.Zero);
                    Helpers.Assert(eh_native_to_managed != IntPtr.Zero);
                    Helpers.Assert(mmch_jit_memcpy != IntPtr.Zero);
                    Helpers.Assert(mmch_jit_hook_config != IntPtr.Zero);
                }
                catch
                {
                    DynDll.CloseLibrary(handle);
                    throw;
                }

                return new JitMemcpyHelper(arch, eh_get_exception_ptr, eh_managed_to_native, eh_native_to_managed, mmch_jit_memcpy, mmch_jit_hook_config);
            }

            /// <summary>
            /// JIT_MAP (RWX) safe memory copy
            /// </summary>
            /// <param name="dst">Destination</param>
            /// <param name="src">Source</param>
            /// <param name="size">Size of memory that should be copied from dst to src</param>
            public unsafe void JitMemCpy(IntPtr dst, IntPtr src, ulong size)
            {
                // TODO: this might be a problem on Mono, not sure yet
                var fnPtr = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ulong, void>)mmch_jit_memcpy;
                fnPtr(dst, src, size);
            }

            /// <summary>
            /// Gets the pointer to the native jit hook configuration struct which can vary by both runtime and arch.
            /// </summary>
            /// <param name="runtimeMajMin">Runtime major and minor version.</param>
            /// <returns>A pointer to the requested jit hook configuration struct.</returns>
            internal unsafe IntPtr GetJitHookConfig(int runtimeMajMin)
            {
                var fnPtr = (delegate* unmanaged[Cdecl]<int, IntPtr>)mmch_jit_hook_config;
                return fnPtr(runtimeMajMin);
            }
        }
    }
}
