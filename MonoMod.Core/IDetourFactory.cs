using System.Reflection;

namespace MonoMod.Core {
    public interface IDetourFactory {

        FeatureFlags SupportedFeatures { get; }

        ICoreDetour CreateDetour(MethodBase source, MethodBase dest);
    }
}
