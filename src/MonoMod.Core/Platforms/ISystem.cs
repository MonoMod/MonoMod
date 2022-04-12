using MonoMod.Core.Utils;

namespace MonoMod.Core.Platforms {
    public interface ISystem {
        OSKind Target { get; }

        SystemFeature Features { get; }

        // the system is initialized with a particular arch
        void Initialize(IArchitecture architecture);
        void PostInit(HostTripleDetourFactory detourFactory);
    }
}
