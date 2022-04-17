using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

        private class PagedMemoryAllocator : IMemoryAllocator {

            private sealed class FreeMem {
                public uint BaseOffset;
                public uint Size;
                public FreeMem? NextFree;
            }

            private sealed class PageAlloc : IAllocatedMemory {

                private readonly Page owner;
                private readonly uint offset;

                public PageAlloc(Page page, uint offset, int size) {
                    owner = page;
                    this.offset = offset;
                    Size = size;
                }

                public IntPtr BaseAddress => (IntPtr)(((nint)owner.BaseAddr) + offset);

                public int Size { get; }

                public unsafe Span<byte> Memory => new((void*) BaseAddress, Size);

                #region IDisposable implementation
                private bool disposedValue;

                private void Dispose(bool disposing) {
                    if (!disposedValue) {

                        owner.FreeMem(offset, (uint)Size);
                        disposedValue = true;
                    }
                }

                ~PageAlloc()
                {
                    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                    Dispose(disposing: false);
                }

                public void Dispose() {
                    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                    Dispose(disposing: true);
                    GC.SuppressFinalize(this);
                }
                #endregion
            }

            private sealed class Page : IDisposable {

                private readonly PagedMemoryAllocator owner;

                public readonly IntPtr BaseAddr;
                public readonly uint Size;

                public readonly bool IsExecutable;

                public int Allocations;

                // we keep freeList sorted by BaseOffset, possibly with nulls between
                private FreeMem? freeList;

                public Page(PagedMemoryAllocator owner, IntPtr baseAddr, uint size, bool isExecutable) {
                    this.owner = owner;
                    (BaseAddr, Size) = (baseAddr, size);
                    IsExecutable = isExecutable;
                    freeList = new FreeMem {
                        BaseOffset = 0,
                        Size = size,
                        NextFree = null,
                    };
                }

                public bool TryAllocate(uint size, uint align, [MaybeNullWhen(false)] out PageAlloc alloc) {
                    // the sizes we allocate we want to round to a power of two
                    //size = BitOperations.RoundUpToPowerOf2(size);

                    ref var ptrNode = ref freeList;

                    uint alignOffset = 0;
                    while (ptrNode is not null) {
                        var alignFix = align - (ptrNode.BaseOffset % align);
                        if (ptrNode.Size >= alignFix + size) {
                            alignOffset = alignFix;
                            break; // we found our node
                        }

                        // otherwise, move to the next one
                        ptrNode = ref ptrNode.NextFree;
                    }

                    if (ptrNode is null) {
                        // we couldn't find a free node large enough
                        alloc = null;
                        return false;
                    }

                    var offs = ptrNode.BaseOffset + alignOffset;

                    if (alignOffset == 0) {
                        // if the align offset is zero, then we just allocate out of the front of this node
                        ptrNode.BaseOffset += size;
                        ptrNode.Size -= size;

                        // removing zero-size enties is done in normalize
                    } else {
                        // otherwise, we have to split the free node

                        // create the front half
                        var frontNode = new FreeMem {
                            BaseOffset = ptrNode.BaseOffset,
                            Size = alignOffset,
                            NextFree = ptrNode,
                        };

                        // push back the back half
                        ptrNode.BaseOffset += alignOffset + size;
                        ptrNode.Size -= alignOffset + size;

                        // removing zero-size enties is done in normalize

                        // update our pointer to point at the new node
                        ptrNode = frontNode;
                    }

                    // now we normalize the free list, to ensure its in a sane state
                    NormalizeFreeList();

                    // and can now actually create the allocation object
                    alloc = new PageAlloc(this, offs, (int) size);
                    Allocations++;

                    return true;
                }

                private void NormalizeFreeList() {
                    ref var node = ref freeList;

                    while (node is not null) {
                        if (node.Size <= 0) {
                            // if the node size is zero, remove it from the list
                            node = node.NextFree;
                            continue; // and retry with new value
                        }

                        if (node.NextFree is { } next &&
                            next.BaseOffset == node.BaseOffset + node.Size) {
                            // if the next node exists and starts at our end, combine down and remove next
                            node.Size += next.Size;
                            node.NextFree = next.NextFree;

                            // now we want to loop back to the top *without* advancing down the chain to continue this
                            continue;
                        }

                        node = ref node.NextFree;
                    }
                }

                // correctness relies on this only being called internally byPageAlloc's Dispose()
                public void FreeMem(uint offset, uint size) {
                    ref var node = ref freeList;

                    while (node is not null) {
                        if (node.BaseOffset > offset) {
                            // we found the first node with greater offset, break out
                            break;
                        }
                        node = ref node.NextFree;
                    }

                    // now node points to where we need to store the new FreeMem, as well as its next node
                    node = new FreeMem {
                        BaseOffset = offset,
                        Size = size,
                        NextFree = node,
                    };
                    NormalizeFreeList();
                }

                #region Disposable implementation
                private bool disposedValue;

                private void Dispose(bool disposing) {
                    if (!disposedValue) {
                        if (disposing) {
                            // TODO: dispose managed state (managed objects)
                        }

                        // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                        // TODO: set large fields to null
                        disposedValue = true;
                    }
                }

                ~Page()
                {
                    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                    Dispose(disposing: false);
                }

                public void Dispose() {
                    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                    Dispose(disposing: true);
                    GC.SuppressFinalize(this);
                }
                #endregion
            }



            public bool TryAllocateInRange(IntPtr target, IntPtr low, IntPtr high, int size, int align, bool executable, [MaybeNullWhen(false)] out IAllocatedMemory allocated) {
                throw new NotImplementedException();
            }
        }

        #region P/Invoke stuff
        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nint dwSize, MEM flAllocationType, PAGE flProtect);

        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, nint dwSize, PAGE flNewProtect, out PAGE lpflOldProtect);

        [DllImport(PlatformDetection.Kernel32, SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, nint dwSize, MEM dwFreeType);

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
        #endregion
    }
}
