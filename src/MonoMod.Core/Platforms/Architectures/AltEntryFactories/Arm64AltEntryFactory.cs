using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Architectures.AltEntryFactories
{
    internal sealed class Arm64AltEntryFactory : IAltEntryFactory
    {
        // Direct branches reach ±128MiB. Keeping the new code within that of the original lets every branch out of it be direct,
        // which avoids clobbering x16 and is required on BTI-guarded pages, where indirect branches must land on BTI instructions.
        // The margin leaves room for the code itself and for branches out of the block that go slightly past the original.
        private const int DirectBranchReach = (128 << 20) - (1 << 20);

        // above this size, the scratch buffer goes on the heap instead of the stack
        private const int MaxStackCodeSize = 1024;

        private readonly ISystem system;
        private readonly IMemoryAllocator alloc;

        public Arm64AltEntryFactory(ISystem system)
        {
            this.system = system;
            alloc = system.MemoryAllocator;
        }

        public unsafe IntPtr CreateAlternateEntrypoint(IntPtr entrypoint, int minLength, out IDisposable? handle)
        {
            ulong srcPc = (nuint)(nint)entrypoint;
            if ((srcPc & 3) != 0)
                throw new ArgumentException("A64 code must be 4-byte aligned", nameof(entrypoint));

            var count = (minLength + 3) / 4;
            var insns = new Arm64Instruction[count];
            for (var i = 0; i < count; i++)
            {
                var raw = *(uint*)((nint)entrypoint + (i * 4));
                insns[i] = Arm64CodeRelocator.Decode(raw, srcPc + (ulong)(i * 4));
            }

            var maxSize = Arm64CodeRelocator.GetMaxEncodedSize(insns);
            var request = new AllocationRequest(maxSize) { Executable = true };

            // prefer direct-branch range of the original; anything further away needs indirect branches
            var low = (nint)entrypoint > DirectBranchReach ? (nint)entrypoint - DirectBranchReach : 0;
            if (!alloc.TryAllocateInRange(new(entrypoint, low, (nint)entrypoint + DirectBranchReach, request), out var allocated))
            {
                MMDbgLog.Warning($"Could not allocate alternate entrypoint for 0x{srcPc:x16} within direct branch range; branches out of it will be indirect");
                Helpers.Assert(alloc.TryAllocate(request, out allocated));
            }

            try
            {
                Span<byte> buffer = maxSize <= MaxStackCodeSize
                    ? stackalloc byte[maxSize]
                    : new byte[maxSize];

                var size = Arm64CodeRelocator.Encode(srcPc, insns, (nuint)(nint)allocated.BaseAddress, buffer);

                // fill the unused tail with brk #0
                for (var i = size; i + 4 <= maxSize; i += 4)
                {
                    buffer[i] = 0x00;
                    buffer[i + 1] = 0x00;
                    buffer[i + 2] = 0x20;
                    buffer[i + 3] = 0xD4;
                }

                system.PatchData(PatchTargetKind.Executable, allocated.BaseAddress, buffer, default);
            }
            catch (Exception e)
            {
                allocated.Dispose();
                MMDbgLog.Error($"Could not create alternate entrypoint for 0x{srcPc:x16}: {e}");
                throw;
            }

            handle = allocated;
            return allocated.BaseAddress;
        }
    }
}
