using MonoMod.Core.Utils;
using System.Reflection;

namespace MonoMod.Core {
    public interface IDetourFactory {

        FeatureFlags SupportedFeatures { get; }

        ICoreDetour CreateDetour(MethodBase source, MethodBase dest);
    }

    public static class DetourFactory {
        private static object creationLock = new();

        private static IDetourFactory? lazyCurrent;
        public static IDetourFactory Current => Helpers.GetOrInitWithLock(ref lazyCurrent, creationLock,
            () => Platforms.HostTripleDetourFactory.CreateCurrent());
    }
}
