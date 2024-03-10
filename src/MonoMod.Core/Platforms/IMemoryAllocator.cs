using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A native memory allocator, capable of allocating memory within certain bounds.
    /// </summary>
    public interface IMemoryAllocator
    {
        /// <summary>
        /// Gets the maximum size of allocation that this allocator is able to allocate.
        /// </summary>
        int MaxSize { get; }

        /// <summary>
        /// Tries to allocate memory according to the provided <see cref="AllocationRequest"/>.
        /// </summary>
        /// <param name="request">The <see cref="AllocationRequest"/> specifying the requested properties of the allocation.</param>
        /// <param name="allocated">The <see cref="IAllocatedMemory"/> instance representing the allocation.</param>
        /// <returns><see langword="true"/> if the allocator was able to allocate memory according to the request; <see langword="false"/> otherwise.</returns>
        bool TryAllocate(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
        /// <summary>
        /// Tries to allocate memory according to the provided <see cref="PositionedAllocationRequest"/>.
        /// </summary>
        /// <param name="request">The <see cref="PositionedAllocationRequest"/> specifying the requested properties of the allocation.</param>
        /// <param name="allocated">The <see cref="IAllocatedMemory"/> instance representing the allocation.</param>
        /// <returns><see langword="true"/> if the allocator was able to allocate memory according to the request; <see langword="false"/> otherwise.</returns>
        bool TryAllocateInRange(PositionedAllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated);
    }

    /// <summary>
    /// A memory allocation request.
    /// </summary>
    /// <param name="Size">The size of the requested allocation.</param>
    public readonly record struct AllocationRequest(int Size)
    {
        /// <summary>
        /// Gets or sets the alignment of the requested allocation. Default is 8.
        /// </summary>
        public int Alignment { get; init; } = 8;

        /// <summary>
        /// Gets or dets whether the allocation should be executable. Default is <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// If an allocation is executable, then it should not be written to directly. Instead, <see cref="ISystem.PatchData(PatchTargetKind, IntPtr, ReadOnlySpan{byte}, Span{byte})"/>
        /// should be used to atomically write a sequence of bytes to that memory.
        /// </remarks>
        public bool Executable { get; init; }
    }

    /// <summary>
    /// A memory allocation request which specifies a set of bounds which the allocation must fall into.
    /// </summary>
    /// <param name="Target">The target address for the allocation. The allocator will attempt to allocate memory as close to this address as it can.</param>
    /// <param name="LowBound">The lower bound of allowed addresses that the allocation may be placed at.</param>
    /// <param name="HighBound">The upper bound of allowed addresses that the allocation may be placed at.</param>
    /// <param name="Base">The <see cref="AllocationRequest"/> specifying all other allocation parameters.</param>
    public readonly record struct PositionedAllocationRequest(IntPtr Target, IntPtr LowBound, IntPtr HighBound, AllocationRequest Base);

    /// <summary>
    /// A single memory allocation from an <see cref="IMemoryAllocator"/>.
    /// </summary>
    /// <remarks>
    /// When this object is disposed, the allocation is freed. Similarly, when the GC collects this object, the allocation is freed.
    /// </remarks>
    public interface IAllocatedMemory : IDisposable
    {
        /// <summary>
        /// Gets whether or not this allocation is executable.
        /// </summary>
        bool IsExecutable { get; }
        /// <summary>
        /// Gets the base address of this allocation.
        /// </summary>
        IntPtr BaseAddress { get; }
        /// <summary>
        /// Gets the size of this allocation.
        /// </summary>
        int Size { get; }
        /// <summary>
        /// Gets a <see cref="Span{T}"/> of the memory allocation.
        /// </summary>
        /// <remarks>
        /// This span should not be written to is <see cref="IsExecutable"/> is <see langword="true"/>. Instead, use
        /// <see cref="ISystem.PatchData(PatchTargetKind, IntPtr, ReadOnlySpan{byte}, Span{byte})"/> to write to the allocation.
        /// </remarks>
        Span<byte> Memory { get; }
    }
}
