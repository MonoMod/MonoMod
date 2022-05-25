using System;

namespace MonoMod.Core {
    public readonly struct FeatureFlags : IEquatable<FeatureFlags> {

        public ArchitectureFeature Architecture { get; }
        public SystemFeature System { get; }
        public RuntimeFeature Runtime { get; }

        public FeatureFlags(ArchitectureFeature archFlags, SystemFeature sysFlags, RuntimeFeature runtimeFlags) {
            Runtime = runtimeFlags;
            Architecture = archFlags;
            System = sysFlags;
        }

        public bool Has(RuntimeFeature feature)
            => (Runtime & feature) == feature;

        public bool Has(ArchitectureFeature feature)
            => (Architecture & feature) == feature;

        public bool Has(SystemFeature feature)
            => (System & feature) == feature;

        public override bool Equals(object? obj) {
            return obj is FeatureFlags flags && Equals(flags);
        }

        public bool Equals(FeatureFlags other) {
            return Runtime == other.Runtime &&
                   Architecture == other.Architecture &&
                   System == other.System;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Runtime, Architecture, System);
        }

        public override string ToString()
            => $"({Architecture})({System})({Runtime})";

        public static bool operator ==(FeatureFlags left, FeatureFlags right) => left.Equals(right);
        public static bool operator !=(FeatureFlags left, FeatureFlags right) => !(left == right);
    }
}
