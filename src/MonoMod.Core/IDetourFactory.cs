using MonoMod.Backports;
using MonoMod.Core.Platforms;
using MonoMod.Core.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core {
    public interface IDetourFactory {

        FeatureFlags SupportedFeatures { get; }

        ICoreDetour CreateDetour(MethodBase source, MethodBase dest);
    }

    public static class DetourFactory {
        // use the actual type for this so that an inlined getter can see the actual type
        private static PlatformTripleDetourFactory? lazyCurrent;
        public static unsafe IDetourFactory Current {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            get => Helpers.GetOrInit(ref lazyCurrent, &CreateDefaultFactory);
        }

        private static PlatformTripleDetourFactory CreateDefaultFactory()
            => new PlatformTripleDetourFactory(PlatformTriple.Current);
    }
}
