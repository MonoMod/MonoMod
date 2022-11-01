using MonoMod.Core.Platforms.Memory;
using MonoMod.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.System.Memory;

namespace MonoMod.Core.Platforms.Systems {
    internal class WindowsSystem : ISystem {
        public OSKind Target => OSKind.Windows;

        public SystemFeature Features => SystemFeature.RWXPages;

        public Abi? DefaultAbi { get; }

        // the classifiers are only called for value types
        private static TypeClassification ClassifyX64(Type type, bool isReturn) {
            var size = type.GetManagedSize();
            if (size is 1 or 2 or 4 or 8) {
                return TypeClassification.InRegister;
            } else {
                return TypeClassification.ByReference;
            }
        }
        private static TypeClassification ClassifyX86(Type type, bool isReturn) {
            if (!isReturn) return TypeClassification.OnStack;

            if (type.GetManagedSize() is 1 or 2 or 4) return TypeClassification.InRegister;
            else return TypeClassification.ByReference;
        }

        public WindowsSystem() {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64) {
                // fastcall
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    ClassifyX64,
                    ReturnsReturnBuffer: true);
            } else if (PlatformDetection.Architecture is ArchitectureKind.x86) {
                // cdecl
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ThisPointer, SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.UserArguments },
                    ClassifyX86,
                    ReturnsReturnBuffer: true);
            }
        }

        // if the provided backup isn't large enough, the data isn't backed up
        public unsafe void PatchData(PatchTargetKind patchKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup) {
            // TODO: should this be thread-safe? It definitely is not right now.

            // Update the protection of this
            if (patchKind == PatchTargetKind.Executable) {
                // Because Windows is Windows, we don't actually need to do anything except make sure we're in RWX
                ProtectRWX(patchTarget, (nuint) data.Length);
            } else {
                ProtectRW(patchTarget, (nuint) data.Length);
            }

            var target = new Span<byte>((void*) patchTarget, data.Length);
            // now we copy target to backup, then data to target, then flush the instruction cache
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            if (patchKind == PatchTargetKind.Executable) {
                FlushInstructionCache(patchTarget, (nuint) data.Length);
            }
        }

        private unsafe static void ProtectRW(IntPtr addr, nuint size) {
            if (!Windows.Win32.Interop.VirtualProtect((void*) addr, size, PAGE_PROTECTION_FLAGS.PAGE_READWRITE, out _)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private unsafe static void ProtectRWX(IntPtr addr, nuint size) {
            if (!Windows.Win32.Interop.VirtualProtect((void*) addr, size, PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE, out _)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private unsafe static void FlushInstructionCache(IntPtr addr, nuint size) {
            if (!Windows.Win32.Interop.FlushInstructionCache(Windows.Win32.Interop.GetCurrentProcess(), (void*) addr, size)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        public unsafe nint GetSizeOfReadableMemory(nint start, nint guess) {
            nint knownSize = 0;

            do {
                if (Interop.Windows.VirtualQuery((void*) start, out var buf, (nuint) sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) == 0) {
                    // VirtualQuery failed, return 0
                    return 0;
                }

                const PAGE_PROTECTION_FLAGS ReadableMask =
                    PAGE_PROTECTION_FLAGS.PAGE_READONLY
                    | PAGE_PROTECTION_FLAGS.PAGE_READWRITE
                    | PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ
                    | PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE;

                var isReadable = (buf.Protect & ReadableMask) != 0;

                if (!isReadable)
                    return knownSize;

                var nextPage = (nint) (buf.BaseAddress + buf.RegionSize);
                knownSize += nextPage - start;
                start = nextPage;
            } while (knownSize < guess);

            return knownSize;
        }

        private static unsafe Exception LogAllSections(int error, IntPtr src, nuint size, [CallerMemberName] string from = "") {
            Exception ex = new Win32Exception(error);
            if (!MMDbgLog.IsWritingLog)
                return ex;

            MMDbgLog.Error($"{from} failed for 0x{src:X16} + {size} - logging all memory sections");
            MMDbgLog.Error($"reason: {ex.Message}");

            try {
                var addr = (IntPtr) 0x00000000000010000;
                var i = 0;
                while (true) {
                    if (Interop.Windows.VirtualQuery((void*) addr, out Interop.Windows.MEMORY_BASIC_INFORMATION infoBasic, (nuint) sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) == 0)
                        break;

                    var srcL = (nuint) (nint) src;
                    var srcR = srcL + size;
                    var infoL = infoBasic.BaseAddress;
                    var infoR = infoL + infoBasic.RegionSize;
                    var overlap = infoL <= srcR && srcL <= infoR;

                    MMDbgLog.Trace($"{(overlap ? "*" : "-")} #{i++}");
                    MMDbgLog.Trace($"addr: 0x{infoBasic.BaseAddress:X16}");
                    MMDbgLog.Trace($"size: 0x{infoBasic.RegionSize:X16}");
                    MMDbgLog.Trace($"aaddr: 0x{infoBasic.AllocationBase:X16}");
                    MMDbgLog.Trace($"state: {infoBasic.State}");
                    MMDbgLog.Trace($"type: {infoBasic.Type}");
                    MMDbgLog.Trace($"protect: {infoBasic.Protect}");
                    MMDbgLog.Trace($"aprotect: {infoBasic.AllocationProtect}");

                    try {
                        IntPtr addrPrev = addr;
                        addr = unchecked((IntPtr) ((ulong) infoBasic.BaseAddress + (ulong) infoBasic.RegionSize));
                        if ((ulong) addr <= (ulong) addrPrev)
                            break;
                    } catch (OverflowException oe) {
                        MMDbgLog.Error($"overflow {oe}");
                        break;
                    }
                }

            } catch {
                throw ex;
            }
            return ex;
        }

        public IMemoryAllocator MemoryAllocator { get; } = new QueryingPagedMemoryAllocator(new PageAllocator());

        private sealed class PageAllocator : QueryingMemoryPageAllocatorBase {
            public override uint PageSize { get; }

            public PageAllocator() {
                Windows.Win32.Interop.GetSystemInfo(out var sysInfo);
                PageSize = sysInfo.dwPageSize;
            }

            public override unsafe bool TryAllocatePage(nint size, bool executable, out IntPtr allocated) {
                var pageProt = executable ? PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE : PAGE_PROTECTION_FLAGS.PAGE_READWRITE;

                allocated = (IntPtr) Windows.Win32.Interop.VirtualAlloc(null, (nuint) size, VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE | VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT, pageProt);
                return allocated != IntPtr.Zero;
            }

            public unsafe override bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated) {
                var pageProt = executable ? PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE : PAGE_PROTECTION_FLAGS.PAGE_READWRITE;

                allocated = (IntPtr) Windows.Win32.Interop.VirtualAlloc((void*) pageAddr, (nuint) size, VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE | VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT, pageProt);
                return allocated != IntPtr.Zero;
            }

            public unsafe override bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg) {
                if (!Windows.Win32.Interop.VirtualFree((void*) pageAddr, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE)) {
                    // VirtualFree failing is kinda wierd, but whatever
                    errorMsg = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                errorMsg = null;
                return true;
            }

            public unsafe override bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize) {
                if (Interop.Windows.VirtualQuery((void*) pageAddr, out var buffer, (nuint) sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) != 0) {
                    isFree = buffer.State == VIRTUAL_ALLOCATION_TYPE.MEM_FREE;
                    allocBase = isFree ? (nint) buffer.BaseAddress : (nint) buffer.AllocationBase;

                    // RegionSize is relative to the provided pageAddr for some reason
                    allocSize = (pageAddr + (nint) buffer.RegionSize) - allocBase;

                    return true;
                } else {
                    isFree = false;
                    allocBase = IntPtr.Zero;
                    allocSize = 0;

                    return false;
                }
            }
        }
    }
}
