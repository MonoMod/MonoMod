using MonoMod.Backports;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.Core.Platforms.Systems {
    internal class WindowsSystem : ISystem {
        public OSKind Target => OSKind.Windows;

        public SystemFeature Features => SystemFeature.RWXPages;

        public Abi? DefaultAbi { get; }

        // the classifiers are only called for value types
        private static TypeClassification ClassifyX64(Type type, bool isReturn) {
            var size = type.GetManagedSize();
            if (size is 1 or 2 or 4 or 8) {
                return TypeClassification.Register;
            } else {
                return TypeClassification.PointerToMemory;
            }
        }

        public WindowsSystem() {
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64) {
                DefaultAbi = new Abi(
                    new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                    ClassifyX64,
                    ReturnsReturnBuffer: true);
            } else {
                // TODO: perform selftests here instead of throwing
                //throw new PlatformNotSupportedException($"Windows on non-x86_64 is currently not supported");
            }
        }

        // if the provided backup isn't large enough, the data isn't backed up
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

            if (patchKind == PatchTargetKind.Executable) {
                FlushInstructionCache(patchTarget, data.Length);
            }
        }

        private static void ProtectRW(IntPtr addr, nint size) {
            if (!Interop.Windows.VirtualProtect(addr, size, Interop.Windows.PAGE.READWRITE, out _)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private static void ProtectRWX(IntPtr addr, nint size) {
            if (!Interop.Windows.VirtualProtect(addr, size, Interop.Windows.PAGE.EXECUTE_READWRITE, out _)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private static void FlushInstructionCache(IntPtr addr, nint size) {
            if (!Interop.Windows.FlushInstructionCache(Interop.Windows.GetCurrentProcess(), addr, size)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private static unsafe Exception LogAllSections(int error, IntPtr src, nint size, [CallerMemberName] string from = "") {
            Exception ex = new Win32Exception(error);
            if (MMDbgLog.Writer == null)
                return ex;

            MMDbgLog.Log($"{from} failed for 0x{src:X16} + {size} - logging all memory sections");
            MMDbgLog.Log($"reason: {ex.Message}");

            try {
                IntPtr addr = (IntPtr) 0x00000000000010000;
                int i = 0;
                while (true) {
                    if (Interop.Windows.VirtualQuery(addr, out Interop.Windows.MEMORY_BASIC_INFORMATION infoBasic, sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) == 0)
                        break;

                    nint srcL = src;
                    nint srcR = srcL + size;
                    nint infoL = (nint) infoBasic.BaseAddress;
                    nint infoR = infoL + (nint) infoBasic.RegionSize;
                    bool overlap = infoL <= srcR && srcL <= infoR;

                    MMDbgLog.Log($"{(overlap ? "*" : "-")} #{i++}");
                    MMDbgLog.Log($"addr: 0x{infoBasic.BaseAddress:X16}");
                    MMDbgLog.Log($"size: 0x{infoBasic.RegionSize:X16}");
                    MMDbgLog.Log($"aaddr: 0x{infoBasic.AllocationBase:X16}");
                    MMDbgLog.Log($"state: {infoBasic.State}");
                    MMDbgLog.Log($"type: {infoBasic.Type}");
                    MMDbgLog.Log($"protect: {infoBasic.Protect}");
                    MMDbgLog.Log($"aprotect: {infoBasic.AllocationProtect}");

                    try {
                        IntPtr addrPrev = addr;
                        addr = unchecked((IntPtr) ((ulong) infoBasic.BaseAddress + (ulong) infoBasic.RegionSize));
                        if ((ulong) addr <= (ulong) addrPrev)
                            break;
                    } catch (OverflowException) {
                        MMDbgLog.Log("overflow");
                        break;
                    }
                }

            } catch {
                throw ex;
            }
            return ex;
        }

        public IMemoryAllocator MemoryAllocator { get; } = new PagedMemoryAllocator(new PageAllocator());

        private sealed class PageAllocator : MemoryPageAllocatorBase {
            public override uint PageSize { get; }

            public PageAllocator() {
                Interop.Windows.GetSystemInfo(out var sysInfo);
                PageSize = sysInfo.dwPageSize;
            }

            public override bool TryAllocatePage(IntPtr pageAddr, nint size, bool executable, out IntPtr allocated) {
                var pageProt = executable ? Interop.Windows.PAGE.EXECUTE_READWRITE : Interop.Windows.PAGE.READWRITE;

                allocated = Interop.Windows.VirtualAlloc(pageAddr, size, Interop.Windows.MEM.RESERVE | Interop.Windows.MEM.COMMIT, pageProt);
                return allocated != IntPtr.Zero;
            }

            public override bool TryFreePage(IntPtr pageAddr, [NotNullWhen(false)] out string? errorMsg) {
                if (!Interop.Windows.VirtualFree(pageAddr, 0, Interop.Windows.MEM.RELEASE)) {
                    // VirtualFree failing is kinda wierd, but whatever
                    errorMsg = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                errorMsg = null;
                return true;
            }

            public unsafe override bool TryQueryPage(IntPtr pageAddr, out bool isFree, out IntPtr allocBase, out nint allocSize) {
                if (Interop.Windows.VirtualQuery(pageAddr, out var buffer, sizeof(Interop.Windows.MEMORY_BASIC_INFORMATION)) != 0) {
                    isFree = buffer.State == Interop.Windows.MEM.FREE;
                    allocBase = isFree ? buffer.BaseAddress : buffer.AllocationBase;

                    // RegionSize is relative to the provided pageAddr for some reason
                    allocSize = ((nint) pageAddr + buffer.RegionSize) - allocBase;

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
