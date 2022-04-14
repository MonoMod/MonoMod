using MonoMod.Core.Utils;
using System.Reflection;

namespace MonoMod.Core {
    public interface IDetourFactory {

        FeatureFlags SupportedFeatures { get; }

        ICoreDetour CreateDetour(MethodBase source, MethodBase dest);
    }

    public static class DetourFactory {
        private static IDetourFactory? lazyCurrent;
        public static unsafe IDetourFactory Current => Helpers.GetOrInit(ref lazyCurrent, &CreateDefaultFactory);

        private static IDetourFactory CreateDefaultFactory()
            => new Platforms.PlatformTripleDetourFactory(Platforms.PlatformTriple.Current);
    }
}
