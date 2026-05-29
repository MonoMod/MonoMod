using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    /// <summary>
    /// Regression test for the Unity Mono + Proton silent patch-loss bug.
    ///
    /// On x86-64, <c>Rel32Ind64</c> places its pointer cell in a separately-allocated page.
    /// Under Unity Mono + Proton, Wine's <c>VirtualQuery</c> reports Mono's JIT memory
    /// reservation as <c>MEM_FREE</c> because Mono reserved it via a direct Linux
    /// <c>mmap</c> syscall that bypasses Wine's virtual-memory tracking.  MonoMod's
    /// allocator therefore places the cell inside that reservation.  When Mono's JIT later
    /// commits or decommits pages in the same range the cell is overwritten with zeros, so
    /// the indirect <c>JMP [cell]</c> jumps to null and Mono raises
    /// <c>NullReferenceException</c> at the patched method's entry point (+0x00000).
    /// </summary>
    public sealed class MonoDetourPointerCellTest : TestBase
    {
        public MonoDetourPointerCellTest(ITestOutputHelper helper) : base(helper) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AnchorMethod() { GC.KeepAlive(null); }

        [Fact]
        public void Rel32Ind64_pointer_cell_is_silently_zeroed_when_page_is_reclaimed()
        {
            if (PlatformDetection.Architecture != ArchitectureKind.x86_64)
                return;

            var anchorMethod = typeof(MonoDetourPointerCellTest)
                .GetMethod(nameof(AnchorMethod),
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic)!;
            RuntimeHelpers.PrepareMethod(anchorMethod.MethodHandle);

            var patchSite = (nint)anchorMethod.MethodHandle.GetFunctionPointer();

            // A target beyond ±2 GB forces Rel32Ind64 (6-byte FF 25 + separate pointer cell).
            var to = patchSite + (nint)(3L * 1024 * 1024 * 1024);

            var info = PlatformTriple.Current.Architecture.ComputeDetourInfo(patchSite, to);

            try
            {
                if (info.InternalKind.Size != 6)
                    return; // fell back to Abs64 (cell is inline) — vulnerability not applicable

                var cell = (IAllocatedMemory)info.InternalData!;
                var cellAddr = cell.BaseAddress;

                var stored = Marshal.ReadIntPtr(cellAddr);

                // Under Unity Mono + Proton this fails: the cell was allocated inside
                // Mono's JIT reservation (Wine's VirtualQuery reported it as MEM_FREE),
                // and Mono immediately committed new code over the same page, zeroing
                // the cell before we could even read it back.
                //
                // With the fix (Abs64 on Mono), ComputeDetourInfo returns a 14-byte kind
                // with InternalData == null, so we return early above and the test passes.
                if (stored != (IntPtr)to)
                    throw new Exception(
                        $"Rel32Ind64 pointer cell at 0x{cellAddr:x} was overwritten: " +
                        $"expected 0x{to:x} but got 0x{stored:x}. " +
                        "Mono's JIT memory manager reused the page that holds the cell, " +
                        "silently breaking the detour.");
            }
            finally
            {
                info.InternalData?.Dispose();
            }
        }

    }
}
