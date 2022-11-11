using MonoMod.Backports;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core {
    [CLSCompliant(true)]
    public interface IDetourFactory {

        FeatureFlags SupportedFeatures { get; }

        ICoreDetour CreateDetour(CreateDetourRequest request);

        ICoreNativeDetour CreateNativeDetour(CreateNativeDetourRequest request);

    }

    [CLSCompliant(true)]
    public readonly record struct CreateDetourRequest(MethodBase Source, MethodBase Target) {
        public bool ApplyByDefault { get; init; } = true;
    }

    [CLSCompliant(true)]
    public readonly record struct CreateNativeDetourRequest(IntPtr Source, IntPtr Target) {
        public bool ApplyByDefault { get; init; } = true;
    }

    [CLSCompliant(true)]
    public static class DetourFactory {
        // use the actual type for this so that an inlined getter can see the actual type
        private static PlatformTripleDetourFactory? lazyCurrent;
        public static unsafe IDetourFactory Current {
            [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
            get => Helpers.GetOrInit(ref lazyCurrent, &CreateDefaultFactory);
        }

        private static PlatformTripleDetourFactory CreateDefaultFactory()
            => new PlatformTripleDetourFactory(PlatformTriple.Current);

        public static ICoreDetour CreateDetour(this IDetourFactory factory, MethodBase source, MethodBase target, bool applyByDefault = true) {
            Helpers.ThrowIfArgumentNull(factory);
            return factory.CreateDetour(new(source, target) { ApplyByDefault = applyByDefault });
        }

        public static ICoreNativeDetour CreateNativeDetour(this IDetourFactory factory, IntPtr source, IntPtr target, bool applyByDefault = true) {
            Helpers.ThrowIfArgumentNull(factory);
            return factory.CreateNativeDetour(new(source, target) { ApplyByDefault = applyByDefault });
        }
    }
}
