using Microsoft.Win32.SafeHandles;
using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using static MonoMod.Core.Interop.OSX;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class MacOSSystem : ISystem, IInitialize<IArchitecture>
    {
        public OSKind Target => OSKind.OSX;

        public SystemFeature Features => SystemFeature.RXPages | SystemFeature.RWXPages;

        public Abi? DefaultAbi { get; }

        public MacOSSystem()
        {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64)
            {
                // As best I can find (Apple docs are worthless) MacOS uses SystemV on x64
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    SystemVABI.ClassifyAMD64,
                    true
                );
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public unsafe IEnumerable<string?> EnumerateLoadedModuleFiles()
        {
            var infoCnt = task_dyld_info.Count;
            var dyldInfo = default(task_dyld_info);
            var kr = task_info(mach_task_self(), task_flavor_t.DyldInfo, &dyldInfo, &infoCnt);
            if (!kr)
            {
                return ArrayEx.Empty<string>(); // could not get own dyld info
            }

            var infos = dyldInfo.all_image_infos->InfoArray;

            var arr = new string?[infos.Length];
            for (var i = 0; i < arr.Length; i++)
            {
                arr[i] = infos[i].imageFilePath.ToString();
            }

            return arr;
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
                // TODO: figure out if MAP_JIT is available and necessary, and use that instead when needed
                MakePageWritable(patchTarget);
            }

            // at this point, we know our data to be writable

            // now we copy target to backup, then data to target
            var target = new Span<byte>((void*)patchTarget, data.Length);
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            // if we got here when executable (either because the memory was already writable or we were able to make it writable) we need to flush the icache
            if (memIsExec)
            {
                sys_icache_invalidate((void*)patchTarget, (nuint)data.Length);
            }
        }

        private static unsafe void MakePageWritable(nint addrInPage)
        {
            Helpers.Assert(GetLocalRegionInfo(addrInPage, out var allocStart, out var allocSize, out var allocProt, out var allocMaxProt));
            Helpers.Assert(allocStart <= addrInPage);

            if (allocProt.Has(vm_prot_t.Write))
                return;

            kern_return_t kr;

            var selfTask = mach_task_self();

            if (allocMaxProt.Has(vm_prot_t.Write))
            {
                kr = mach_vm_protect(selfTask, (ulong)allocStart, (ulong)allocSize, false, allocProt | vm_prot_t.Write);
                if (!kr)
                {
                    MMDbgLog.Error($"Could not vm_protect page 0x{allocStart:x16}+0x{allocSize:x} " +
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
                    MMDbgLog.Error($"Requested 0x{allocStart:x16}+0x{allocSize:x} (max: {P(allocMaxProt)}) to be made writable, but its not readable!");
                    throw new NotSupportedException("Cannot make page writable because its not readable");
                }
                kr = mach_vm_protect(selfTask, (ulong)allocStart, (ulong)allocSize, false, allocProt | vm_prot_t.Read);
                if (!kr)
                {
                    MMDbgLog.Error($"vm_protect of 0x{allocStart:x16}+0x{allocSize:x} (max: {P(allocMaxProt)}) to become readable failed: kr = {kr.Value}");
                    throw new NotSupportedException("Could not make page readable for remap");
                }
            }

            MMDbgLog.Trace($"Performing page remap on 0x{allocStart:x16}+0x{allocSize:x} from {P(allocProt)}/{P(allocMaxProt)} to {P(allocProt | vm_prot_t.Write)}");

            var wantProt = allocProt | vm_prot_t.Write;
            var wantMaxProt = allocMaxProt | vm_prot_t.Write;

            // first, alloc a new page
            ulong newAddr;
            kr = mach_vm_map(selfTask, &newAddr, (ulong)allocSize, 0, vm_flags.Anywhere, 0, 0, true, wantProt, wantMaxProt, vm_inherit_t.Default);
            if (!kr)
            {
                MMDbgLog.Error($"Could not allocate new memory! kr = {kr.Value}");
#pragma warning disable CA2201 // Do not raise reserved exception types
                throw new OutOfMemoryException();
#pragma warning restore CA2201 // Do not raise reserved exception types
            }

            try
            {
                // then copy data from the map into it
                new Span<byte>((void*)allocStart, (int)allocSize).CopyTo(new Span<byte>((void*)newAddr, (int)allocSize));
                // then create an object for that memory
                int obj;
                var memSize = (ulong)allocSize;
                kr = mach_make_memory_entry_64(selfTask, &memSize, newAddr, wantMaxProt, &obj, 0);
                if (!kr)
                {
                    MMDbgLog.Error($"make_memory_entry(task_self(), size: 0x{memSize:x}, addr: {newAddr:x16}, prot: {P(wantMaxProt)}, &obj, 0) failed: kr = {kr.Value}");
                    throw new NotSupportedException("make_memory_entry() failed");
                }
                // then map it over the old memory segment
                var targetAddr = (ulong)allocStart;
                kr = mach_vm_map(selfTask, &targetAddr, (ulong)allocSize, 0, vm_flags.Fixed | vm_flags.Overwrite, obj, 0, true, wantProt, wantMaxProt, vm_inherit_t.Default);
                if (!kr)
                {
                    MMDbgLog.Error($"vm_map() failed to map over target range: 0x{targetAddr:x16}+0x{allocSize:x} ({P(allocProt)}/{P(allocMaxProt)})" +
                        $" <- (obj {obj}) 0x{newAddr:x16}+0x{allocSize:x} ({P(wantProt)}/{P(wantMaxProt)}), kr = {kr.Value}");
                    throw new NotSupportedException("vm_map() failed");
                }
            }
            finally
            {
                // then unmap the created memory
                kr = mach_vm_deallocate(selfTask, newAddr, (ulong)allocSize);
                if (!kr)
                {
                    MMDbgLog.Error($"Could not deallocate created memory page 0x{newAddr:x16}+0x{allocSize:x}! kr = {kr.Value}");
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

        private static ReadOnlySpan<byte> NEHTempl => "/tmp/mm-exhelper.dylib.XXXXXX"u8;

        private unsafe PosixExceptionHelper CreateNativeExceptionHelper()
        {
            Helpers.Assert(arch is not null);

            var soname = arch.Target switch
            {
                ArchitectureKind.x86_64 => "exhelper_macos_x86_64.dylib",
                _ => throw new NotImplementedException($"No exception helper for current arch")
            };

            // we want to get a temp file, write our helper to it, and load it
            var templ = ArrayPool<byte>.Shared.Rent(NEHTempl.Length + 1);
            int fd;
            string fname;
            try
            {
                templ.AsSpan().Clear();
                NEHTempl.CopyTo(templ);

                fixed (byte* pTmpl = templ)
                    fd = MkSTemp(pTmpl);

                if (fd == -1)
                {
                    var lastError = OSX.Errno;
                    var ex = new Win32Exception(lastError);
                    MMDbgLog.Error($"Could not create temp file for NativeExceptionHelper: {lastError} {ex}");
                    throw ex;
                }

                fname = Encoding.UTF8.GetString(templ, 0, NEHTempl.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(templ);
            }


            using (var fh = new SafeFileHandle((IntPtr)fd, true))
            using (var fs = new FileStream(fh, FileAccess.Write))
            {
                using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(soname);
                Helpers.Assert(embedded is not null);

                embedded.CopyTo(fs);
            }
            return PosixExceptionHelper.CreateHelper(arch, fname);
        }
    }
}
