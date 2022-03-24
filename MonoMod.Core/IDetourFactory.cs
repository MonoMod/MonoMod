using System.Reflection;

namespace MonoMod.Core {
    public interface IDetourFactory {

        FeatureFlags SupportedFeatures { get; }

        CoreDetour CreateDetour(MethodBase source, MethodBase dest);
    }
}
