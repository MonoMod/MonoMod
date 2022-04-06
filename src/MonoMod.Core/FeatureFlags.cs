using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core {
    public readonly struct FeatureFlags : IEquatable<FeatureFlags> {

        public RuntimeFeature Runtime { get; }

        public ArchitectureFeature Architecture { get; }

        public SystemFeature System { get; }

        public FeatureFlags(RuntimeFeature runtimeFlags, ArchitectureFeature archFlags, SystemFeature sysFlags) {
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

        public static bool operator ==(FeatureFlags left, FeatureFlags right) => left.Equals(right);
        public static bool operator !=(FeatureFlags left, FeatureFlags right) => !(left == right);
    }
}
