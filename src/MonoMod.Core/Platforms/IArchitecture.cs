using MonoMod.Core.Utils;

namespace MonoMod.Core.Platforms {
    public interface IArchitecture {
        Architecture Target { get; }
        ArchitectureFeature Features { get; }

        BytePatternCollection KnownMethodThunks { get; }
    }
}
