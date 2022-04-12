using MonoMod.Core.Utils;

namespace MonoMod.Core.Platforms {
    public interface IArchitecture {
        ArchitectureKind Target { get; }
        ArchitectureFeature Features { get; }

        BytePatternCollection KnownMethodThunks { get; }

        // initialization is done entirely through construction
    }
}
