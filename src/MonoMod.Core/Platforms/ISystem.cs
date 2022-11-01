using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms {
    public interface ISystem {
        OSKind Target { get; }

        SystemFeature Features { get; }

        Abi? DefaultAbi { get; }

        IMemoryAllocator MemoryAllocator { get; }

        nint GetSizeOfReadableMemory(IntPtr start, nint guess);

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <paramref name="targetKind"/> is to be used as nothing more than a hint. The system should check the existing mapping
        /// to ensure that it is in fact correct (so that it would not remap an RW page to an RX page, or something similar).
        /// </remarks>
        /// <param name="targetKind"></param>
        /// <param name="patchTarget"></param>
        /// <param name="data"></param>
        /// <param name="backup"></param>
        void PatchData(PatchTargetKind targetKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup);
    }

    public enum PatchTargetKind {
        Executable,
        ReadOnly,
    }
}
