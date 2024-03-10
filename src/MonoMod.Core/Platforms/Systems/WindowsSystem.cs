using MonoMod.Core.Platforms.Memory;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using static MonoMod.Core.Interop.Windows;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class WindowsSystem : ISystem
    {
        public OSKind Target => OSKind.Windows;

        public SystemFeature Features => SystemFeature.RWXPages;

        // Windows doesn't need an exception helper
        public INativeExceptionHelper? NativeExceptionHelper => null;

        public Abi? DefaultAbi { get; }

        // the classifiers are only called for value types
        private static TypeClassification ClassifyX64(Type type, bool isReturn)
        {
            var size = type.GetManagedSize();
            if (size is 1 or 2 or 4 or 8)
            {
                return TypeClassification.InRegister;
            }
            else
            {
                return TypeClassification.ByReference;
            }
        }
        private static TypeClassification ClassifyX86(Type type, bool isReturn)
        {
            if (!isReturn)
                return TypeClassification.OnStack;

            if (type.GetManagedSize() is 1 or 2 or 4)
                return TypeClassification.InRegister;
            else
                return TypeClassification.ByReference;
        }

        public WindowsSystem()
        {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64)
            {
                // fastcall
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    ClassifyX64,
                    ReturnsReturnBuffer: true);
            }
            else if (PlatformDetection.Architecture is ArchitectureKind.x86)
            {
                // cdecl
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.UserArguments },
                    ClassifyX86,
                    ReturnsReturnBuffer: true);
            }
        }

        // if the provided backup isn't large enough, the data isn't backed up
        public unsafe void PatchData(PatchTargetKind patchKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup)
        {
            // TODO: should this be thread-safe? It definitely is not right now.

            // Update the protection of this
            if (patchKind == PatchTargetKind.Executable)
            {
                // Because Windows is Windows, we don't actually need to do anything except make sure we're in RWX
                ProtectRWX(patchTarget, (nuint)data.Length);
            }
            else
            {
                ProtectRW(patchTarget, (nuint)data.Length);
            }

            var target = new Span<byte>((void*)patchTarget, data.Length);
            // now we copy target to backup, then data to target, then flush the instruction cache
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            if (patchKind == PatchTargetKind.Executable)
            {
                FlushInstructionCache(patchTarget, (nuint)data.Length);
            }
        }

        private unsafe static void ProtectRW(IntPtr addr, nuint size)
        {
            uint oldProtect;
            if (!VirtualProtect((void*)addr, size, PAGE_READWRITE, &oldProtect))
            {
                throw LogAllSections(GetLastError(), addr, size);
            }
        }

        private unsafe static void ProtectRWX(IntPtr addr, nuint size)
        {
            uint oldProtect;
            if (!VirtualProtect((void*)addr, size, PAGE_EXECUTE_READWRITE, &oldProtect))
            {
                throw LogAllSections(GetLastError(), addr, size);
            }
        }

        private unsafe static void FlushInstructionCache(IntPtr addr, nuint size)
        {
            if (!Interop.Windows.FlushInstructionCache(GetCurrentProcess(), (void*)addr, size))
            {
                throw LogAllSections(GetLastError(), addr, size);
            }
        }

        public IEnumerable<string?> EnumerateLoadedModuleFiles()
        {
            return Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m => m.FileName)!;
        }

        public unsafe nint GetSizeOfReadableMemory(nint start, nint guess)
        {
            nint knownSize = 0;

            do
            {
                MEMORY_BASIC_INFORMATION buf;
                if (VirtualQuery((void*)start, &buf, (nuint)sizeof(MEMORY_BASIC_INFORMATION)) == 0)
                {
                    var lastError = GetLastError();
                    MMDbgLog.Warning($"VirtualQuery failed: {lastError} {new Win32Exception((int)lastError).Message}");
                    // VirtualQuery failed, return 0
                    return 0;
                }

                MMDbgLog.Spam($"VirtualQuery(0x{start:x16}) == {{ Protect = {buf.Protect:x}, BaseAddr = {(nuint)buf.BaseAddress:x16}, Size = {buf.RegionSize:x4} }}");

                const uint ReadableMask =
                    PAGE_READONLY
                    | PAGE_READWRITE
                    | PAGE_EXECUTE_READ
                    | PAGE_EXECUTE_READWRITE;

                var isReadable = (buf.Protect & ReadableMask) != 0;

                if (!isReadable)
                    return knownSize;

                var nextPage = (nint)((nuint)buf.BaseAddress + buf.RegionSize);
                knownSize += nextPage - start;
                start = nextPage;
            } while (knownSize < guess);

            return knownSize;
        }

        private static unsafe Exception LogAllSections(uint error, IntPtr src, nuint size, [CallerMemberName] string from = "")
        {
            Exception ex = new Win32Exception((int)error);
            if (!MMDbgLog.IsWritingLog)
                return ex;

            MMDbgLog.Error($"{from} failed for 0x{src:X16} + {size} - logging all memory sections");
            MMDbgLog.Error($"reason: {ex.Message}");

            try
            {
                var addr = (IntPtr)0x00000000000010000;
                var i = 0;
                while (true)
                {
                    MEMORY_BASIC_INFORMATION infoBasic;
                    if (VirtualQuery((void*)addr, &infoBasic, (nuint)sizeof(MEMORY_BASIC_INFORMATION)) == 0)
                        break;

                    var srcL = (nuint)(nint)src;
                    var srcR = srcL + size;
                    var infoL = (nuint)infoBasic.BaseAddress;
                    var infoR = infoL + infoBasic.RegionSize;
                    var overlap = infoL <= srcR && srcL <= infoR;

                    MMDbgLog.Trace($"{(overlap ? "*" : "-")} #{i++}");
                    MMDbgLog.Trace($"addr: 0x{(nuint)infoBasic.BaseAddress:X16}");
                    MMDbgLog.Trace($"size: 0x{infoBasic.RegionSize:X16}");
                    MMDbgLog.Trace($"aaddr: 0x{(nuint)infoBasic.AllocationBase:X16}");
                    MMDbgLog.Trace($"state: {infoBasic.State}");
                    MMDbgLog.Trace($"type: {infoBasic.Type}");
                    MMDbgLog.Trace($"protect: {infoBasic.Protect}");
                    MMDbgLog.Trace($"aprotect: {infoBasic.AllocationProtect}");

                    try
                    {
                        var addrPrev = addr;
                        addr = unchecked((IntPtr)((nuint)infoBasic.BaseAddress + (ulong)infoBasic.RegionSize));
                        if ((ulong)addr <= (ulong)addrPrev)
                            break;
                    }
                    catch (OverflowException oe)
                    {
                        MMDbgLog.Error($"overflow {oe}");
                        break;
                    }
                }

            }
            catch
            {
                throw ex;
            }
            return ex;
        }

        public IMemoryAllocator MemoryAllocator { get; } = new QueryingPagedMemoryAllocator(new PageAllocator());

        private sealed class PageAllocator : QueryingMemoryPageAllocatorBase
        {
            public override uint PageSize { get; }

            public PageAllocator()
            {
                SYSTEM_INFO sysInfo;
                unsafe { GetSystemInfo(&sysInfo); }
                PageSize = sysInfo.dwPageSize;
            }

            public override unsafe bool TryAllocatePage(nint size, bool executable, out IntPtr allocated)
            {
                var pageProt = executable ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE;

                allocated = (IntPtr)VirtualAlloc(null, (nuint)size, MEM_RESERVE | MEM_COMMIT, (uint)pageProt);
                return allocated != IntPtr.Zero;
            }

            public unsafe override bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated)
            {
                var pageProt = executable ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE;

                allocated = (IntPtr)VirtualAlloc((void*)pageAddr, (nuint)size, MEM_RESERVE | MEM_COMMIT, (uint)pageProt);
                return allocated != IntPtr.Zero;
            }

            public unsafe override bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg)
            {
                if (!VirtualFree((void*)pageAddr, 0, MEM_RELEASE))
                {
                    // VirtualFree failing is kinda wierd, but whatever
                    errorMsg = new Win32Exception((int)GetLastError()).Message;
                    return false;
                }

                errorMsg = null;
                return true;
            }

            public unsafe override bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize)
            {
                MEMORY_BASIC_INFORMATION buffer;
                if (Interop.Windows.VirtualQuery((void*)pageAddr, &buffer, (nuint)sizeof(MEMORY_BASIC_INFORMATION)) != 0)
                {
                    isFree = buffer.State == MEM_FREE;
                    allocBase = isFree ? (nint)buffer.BaseAddress : (nint)buffer.AllocationBase;

                    // RegionSize is relative to the provided pageAddr for some reason
                    allocSize = (pageAddr + (nint)buffer.RegionSize) - allocBase;

                    return true;
                }
                else
                {
                    isFree = false;
                    allocBase = IntPtr.Zero;
                    allocSize = 0;

                    return false;
                }
            }
        }
    }
}
