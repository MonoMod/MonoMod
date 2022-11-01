using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms {
    public interface IMemoryAllocator {
        int MaxSize { get; }

        bool TryAllocate(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
        bool TryAllocateInRange(PositionedAllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
    }

    public readonly record struct PositionedAllocationRequest(IntPtr Target, IntPtr LowBound, IntPtr HighBound, AllocationRequest Base);

    public readonly record struct AllocationRequest(int Size) {
        public int Alignment { get; init; } = 8;

        [SuppressMessage("Performance", "CA1805:Do not initialize unnecessarily",
            Justification = "This is required because this is a record.")]
        public bool Executable { get; init; } = false;
    }

    public interface IAllocatedMemory : IDisposable {
        bool IsExecutable { get; }
        IntPtr BaseAddress { get; }
        int Size { get; }
        Span<byte> Memory { get; }
    }
}
