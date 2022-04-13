using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms {
    public interface IArchitecture {
        ArchitectureKind Target { get; }
        ArchitectureFeature Features { get; }

        BytePatternCollection KnownMethodThunks { get; }

        NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr to);
        /// <summary>
        /// Gets the actual bytes making up the specified detour.
        /// </summary>
        /// <param name="info">The <see cref="NativeDetourInfo"/> representing the detour.</param>
        /// <param name="buffer">A buffer which will hold the byte sequence. It must be at least <see cref="NativeDetourInfo.Size"/> bytes in length.</param>
        /// <returns>The number of bytes written to the buffer.</returns>
        int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer);
    }

    // TODO: somehow support trampolines
    // maybe by replacing InternalKind with an opaque object?
    public readonly record struct NativeDetourInfo(IntPtr From, IntPtr To, int Size, int InternalKind);
}
