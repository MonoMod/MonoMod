using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// Represents a host architecture.
    /// </summary>
    public interface IArchitecture
    {
        /// <summary>
        /// Gets the <see cref="ArchitectureKind"/> that this instance represents.
        /// </summary>
        ArchitectureKind Target { get; }
        /// <summary>
        /// Gets the set of <see cref="ArchitectureFeature"/>s that this instance supports. Some members may only be available with certain feature flags set.
        /// </summary>
        ArchitectureFeature Features { get; }

        /// <summary>
        /// Gets a <see cref="BytePatternCollection"/> containing known method thunks for this architecture. These are used to locate the real method entry point.
        /// </summary>
        BytePatternCollection KnownMethodThunks { get; }

        /// <summary>
        /// Gets the <see cref="IAltEntryFactory"/> for this architecture.
        /// </summary>
        /// <remarks>
        /// <para>This must only be accessed if <see cref="Features"/> includes <see cref="ArchitectureFeature.CreateAltEntryPoint"/>.</para>
        /// </remarks>
        IAltEntryFactory AltEntryFactory { get; }

        /// <summary>
        /// Computes a <see cref="NativeDetourInfo"/> which can be used to patch the instructions at <paramref name="from"/> to jump to <paramref name="target"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The typical calling sequence is <see cref="ComputeDetourInfo(IntPtr, IntPtr, int)"/>, allocate a buffer, <see cref="GetDetourBytes(NativeDetourInfo, Span{byte}, out IDisposable?)"/>,
        /// then <see cref="ISystem.PatchData(PatchTargetKind, IntPtr, ReadOnlySpan{byte}, Span{byte})"/> to apply the detour.
        /// </para>
        /// <para>Usually, callers should use <see cref="PlatformTriple.CreateSimpleDetour(IntPtr, IntPtr, int, IntPtr)"/> or
        /// <see cref="PlatformTriple.CreateNativeDetour(IntPtr, IntPtr, int, IntPtr)"/> instead.</para>
        /// </remarks>
        /// <param name="from">The address to detour from.</param>
        /// <param name="target">The address to detour to.</param>
        /// <param name="maxSizeHint">The maximum amount of readable/writable memory at <paramref name="from"/>, or -1. This is to be used as a hint, and may be ignored.</param>
        /// <returns>The computed <see cref="NativeDetourInfo"/>.</returns>
        /// <seealso cref="GetDetourBytes(NativeDetourInfo, Span{byte}, out IDisposable?)"/>
        /// <seealso cref="ISystem.PatchData"/>
        /// <seealso cref="PlatformTriple.CreateSimpleDetour(IntPtr, IntPtr, int, IntPtr)"/>
        /// <seealso cref="PlatformTriple.CreateNativeDetour(IntPtr, IntPtr, int, IntPtr)"/>
        NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr target, int maxSizeHint = -1);
        /// <summary>
        /// Gets the actual bytes making up the specified detour.
        /// </summary>
        /// <param name="info">The <see cref="NativeDetourInfo"/> representing the detour.</param>
        /// <param name="buffer">A buffer which will hold the byte sequence. It must be at least <see cref="NativeDetourInfo.Size"/> bytes in length.</param>
        /// <param name="allocationHandle">A handle to any allocation which must stay alive with the detour.</param>
        /// <returns>The number of bytes written to the buffer.</returns>
        /// <seealso cref="ComputeDetourInfo(IntPtr, IntPtr, int)"/>
        /// <seealso cref="ISystem.PatchData"/>
        /// <seealso cref="PlatformTriple.CreateSimpleDetour(IntPtr, IntPtr, int, IntPtr)"/>
        /// <seealso cref="PlatformTriple.CreateNativeDetour(IntPtr, IntPtr, int, IntPtr)"/>
        int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocationHandle);

        /// <summary>
        /// Computes a <see cref="NativeDetourInfo"/> which can be used to retarget <paramref name="detour"/> to instead jump to <paramref name="target"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The typical calling sequence is <see cref="ComputeRetargetInfo(NativeDetourInfo, IntPtr, int)"/>, allocate a buffer,
        /// <see cref="GetRetargetBytes(NativeDetourInfo, NativeDetourInfo, Span{byte}, out IDisposable?, out bool, out bool)"/>,
        /// then if (and ONLY if) <c>needsRepatch</c> is <see langword="true"/>, <see cref="ISystem.PatchData(PatchTargetKind, IntPtr, ReadOnlySpan{byte}, Span{byte})"/>
        /// to patch the data in. Then, if <c>disposeOldAlloc</c> is <see langword="true"/>, dispose the allocations associated with the original detour.
        /// </para>
        /// <para>Usually, callers should use <see cref="SimpleNativeDetour.ChangeTarget(IntPtr)"/> instead.</para>
        /// </remarks>
        /// <param name="detour">The detour to retarget.</param>
        /// <param name="target">The new target of the detour.</param>
        /// <param name="maxSizeHint">The maximum amount of readable/writable memory at the detour site, or -1. This is to be used as a hint, and may be ignored.</param>
        /// <returns>The computed <see cref="NativeDetourInfo"/>.</returns>
        /// <seealso cref="GetRetargetBytes(NativeDetourInfo, NativeDetourInfo, Span{byte}, out IDisposable?, out bool, out bool)"/>
        /// <seealso cref="ISystem.PatchData"/>
        /// <seealso cref="SimpleNativeDetour.ChangeTarget(IntPtr)"/>
        NativeDetourInfo ComputeRetargetInfo(NativeDetourInfo detour, IntPtr target, int maxSizeHint = -1);
        /// <summary>
        /// Gets the actual bytes to apply to perform the retarget.
        /// </summary>
        /// <param name="original">The original detour being retargeted.</param>
        /// <param name="retarget">The <see cref="NativeDetourInfo"/> returned by <see cref="ComputeRetargetInfo(NativeDetourInfo, IntPtr, int)"/> for this retarget.</param>
        /// <param name="buffer">The buffer to write the patch data into. This must be at least <see cref="NativeDetourInfo.Size"/> (of <paramref name="retarget"/> bytes in length.</param>
        /// <param name="allocationHandle">A handle to any allocation which must stay alive with the detour.</param>
        /// <param name="needsRepatch"><see langword="true"/> if the data in <paramref name="buffer"/> should be patched into source location. If this is <see langword="false"/>,
        /// the data should not be patched in.</param>
        /// <param name="disposeOldAlloc"><see langword="true"/> if allocations associated with the old detour should be disposed. If this is <see langword="false"/>, then the old
        /// allocations should not be disposed, but do not necessarily need to be kept alive.</param>
        /// <returns>The number of bytes written to the buffer.</returns>
        /// <seealso cref="ComputeRetargetInfo(NativeDetourInfo, IntPtr, int)"/>
        /// <seealso cref="ISystem.PatchData"/>
        /// <seealso cref="SimpleNativeDetour.ChangeTarget(IntPtr)"/>
        int GetRetargetBytes(NativeDetourInfo original, NativeDetourInfo retarget, Span<byte> buffer,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc);

        /// <summary>
        /// Populates a native vtable with proxy stubs to an object with the same vtable shape.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The expected layout for the proxy object is this:
        /// <list type="u">
        ///     <item><c><see cref="IntPtr.Size"/> * 0</c></item><description><c> = pVtbl</c> A pointer to the vtable generated by this method.</description>
        ///     <item><c><see cref="IntPtr.Size"/> * 1</c></item><description><c> = pWrapped</c> A pointer to the wrapped object.</description>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="vtableBase">The base pointer for the vtable to fill. This must be large enough to hold <paramref name="vtableSize"/> entries.</param>
        /// <param name="vtableSize">The number of vtable entries to fill.</param>
        /// <returns>A collection of <see cref="IAllocatedMemory"/> which contain the stubs referenced by the generated vtable.</returns>
        ReadOnlyMemory<IAllocatedMemory> CreateNativeVtableProxyStubs(IntPtr vtableBase, int vtableSize);

        /// <summary>
        /// Creates an architecture-specific special entry stub, that passes an extra argument not in the normal calling convention.
        /// </summary>
        /// <param name="target">The target to call.</param>
        /// <param name="argument">The extra argument for that target.</param>
        /// <returns>An <see cref="IAllocatedMemory"/> containing the generated stub.</returns>
        IAllocatedMemory CreateSpecialEntryStub(IntPtr target, IntPtr argument);
    }

    /// <summary>
    /// A native detour kind. The implementation is provided by the <see cref="IArchitecture"/> instance, and exposes only its size, in bytes.
    /// </summary>
    public interface INativeDetourKind
    {
        /// <summary>
        /// Gets the size, in bytes, of this native detour.
        /// </summary>
        int Size { get; }
    }

    /// <summary>
    /// An aggregate which represents a native detour, as returned from <see cref="IArchitecture.ComputeDetourInfo(IntPtr, IntPtr, int)"/> and
    /// <see cref="IArchitecture.ComputeRetargetInfo(NativeDetourInfo, IntPtr, int)"/> and consumed by their duals.
    /// </summary>
    /// <param name="From">The source address of the detour.</param>
    /// <param name="To">The target address of the detour.</param>
    /// <param name="InternalKind">The <see cref="INativeDetourKind"/> for the detour.</param>
    /// <param name="InternalData">A data field which allows the <see cref="IArchitecture"/> instance to persist data through calls. Often, this is a memory allocation handle.</param>
    public readonly record struct NativeDetourInfo(IntPtr From, IntPtr To, INativeDetourKind InternalKind, IDisposable? InternalData)
    {
        /// <summary>
        /// Gets the size, in bytes, of the detour this represents.
        /// </summary>
        public int Size => InternalKind.Size;
    }
}
