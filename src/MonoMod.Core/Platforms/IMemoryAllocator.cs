using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface IMemoryAllocator {
        bool TryAllocateInRange(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
    }

    // TODO: should some of these non-default values be given defaults?
    public readonly record struct AllocationRequest(IntPtr Target, IntPtr LowBound, IntPtr HighBound, int Size) {
        public int Alignment { get; init; } = 8;

        [SuppressMessage("Performance", "CA1805:Do not initialize unnecessarily",
            Justification = "This is required because this is a record.")]
        public bool Executable { get; init; } = false;
    }

    public interface IAllocatedMemory : IDisposable {
        // TODO: distinguish between executable and non-executable
        IntPtr BaseAddress { get; }
        int Size { get; }
        Span<byte> Memory { get; }
    }
}
