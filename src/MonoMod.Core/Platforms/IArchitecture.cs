using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms {
    public interface IArchitecture {
        ArchitectureKind Target { get; }
        ArchitectureFeature Features { get; }

        BytePatternCollection KnownMethodThunks { get; }

        NativeDetourInfo ComputeDetourInfo(IntPtr from, IntPtr to, int maxSizeHint = -1);
        /// <summary>
        /// Gets the actual bytes making up the specified detour.
        /// </summary>
        /// <param name="info">The <see cref="NativeDetourInfo"/> representing the detour.</param>
        /// <param name="buffer">A buffer which will hold the byte sequence. It must be at least <see cref="NativeDetourInfo.Size"/> bytes in length.</param>
        /// <returns>The number of bytes written to the buffer.</returns>
        int GetDetourBytes(NativeDetourInfo info, Span<byte> buffer, out IDisposable? allocationHandle);

        NativeDetourInfo ComputeRetargetInfo(NativeDetourInfo detour, IntPtr to, int maxSizeHint = -1);

        int GetRetargetBytes(NativeDetourInfo original, NativeDetourInfo retarget, Span<byte> buffer,
            out IDisposable? allocationHandle, out bool needsRepatch, out bool disposeOldAlloc);
    }

    public interface INativeDetourKind {
        int Size { get; }
    }

    public readonly record struct NativeDetourInfo(IntPtr From, IntPtr To, INativeDetourKind InternalKind, IDisposable? InternalData) {
        public int Size => InternalKind.Size;
    }
}
