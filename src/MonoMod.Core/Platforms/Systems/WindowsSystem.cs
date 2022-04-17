using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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
        public unsafe void PatchExecutableData(IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup) {
            // TODO: should this be thread-safe? It definitely is not right now.

            // Update the protection of this
            // Because Windows is Windows, we don't actually need to do anything except make sure we're in RWX
            SetProtection(patchTarget, data.Length);

            var target = new Span<byte>((void*) patchTarget, data.Length);
            // now we copy target to backup, then data to target, then flush the instruction cache
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);

            FlushInstructionCache(patchTarget, data.Length);
        }

        private static void SetProtection(IntPtr addr, nint size) {
            if (!VirtualProtect(addr, size, PAGE.EXECUTE_READWRITE, out _)) {
                throw LogAllSections(Marshal.GetLastWin32Error(), addr, size);
            }
        }

        private static void FlushInstructionCache(IntPtr addr, nint size) {
            if (!FlushInstructionCache(GetCurrentProcess(), addr, size)) {
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
                IntPtr proc = GetCurrentProcess();
                IntPtr addr = (IntPtr) 0x00000000000010000;
                int i = 0;
                while (true) {
                    if (VirtualQueryEx(proc, addr, out MEMORY_BASIC_INFORMATION infoBasic, sizeof(MEMORY_BASIC_INFORMATION)) == 0)
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

        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, nint dwSize, PAGE flNewProtect, out PAGE lpflOldProtect);

        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nint dwSize);

        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

        [Flags]
        private enum PAGE : uint {
            UNSET,
            NOACCESS =
                0b00000000000000000000000000000001,
            READONLY =
                0b00000000000000000000000000000010,
            READWRITE =
                0b00000000000000000000000000000100,
            WRITECOPY =
                0b00000000000000000000000000001000,
            EXECUTE =
                0b00000000000000000000000000010000,
            EXECUTE_READ =
                0b00000000000000000000000000100000,
            EXECUTE_READWRITE =
                0b00000000000000000000000001000000,
            EXECUTE_WRITECOPY =
                0b00000000000000000000000010000000,
            GUARD =
                0b00000000000000000000000100000000,
            NOCACHE =
                0b00000000000000000000001000000000,
            WRITECOMBINE =
                0b00000000000000000000010000000000,
        }

        private enum MEM : uint {
            UNSET,
            COMMIT =
                0b00000000000000000001000000000000,
            RESERVE =
                0b00000000000000000010000000000000,
            FREE =
                0b00000000000000010000000000000000,
            PRIVATE =
                0b00000000000000100000000000000000,
            MAPPED =
                0b00000000000001000000000000000000,
            IMAGE =
                0b00000001000000000000000000000000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public PAGE AllocationProtect;
            public IntPtr RegionSize;
            public MEM State;
            public PAGE Protect;
            public MEM Type;
        }
    }
}
