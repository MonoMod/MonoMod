using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms {
    public interface ISystem {
        OSKind Target { get; }

        SystemFeature Features { get; }

        Abi? DefaultAbi { get; }

        void PatchExecutableData(IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup);
    }
}
