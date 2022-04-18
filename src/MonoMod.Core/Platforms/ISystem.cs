using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms {
    public interface ISystem {
        OSKind Target { get; }

        SystemFeature Features { get; }

        Abi? DefaultAbi { get; }

        IMemoryAllocator MemoryAllocator { get; }

        void PatchExecutableData(IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup);
    }
}
