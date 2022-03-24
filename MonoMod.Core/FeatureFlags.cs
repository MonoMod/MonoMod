using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core {
    public readonly struct FeatureFlags {

        public RuntimeFeature Runtime { get; }

        public FeatureFlags(RuntimeFeature runtimeFlags) {
            Runtime = runtimeFlags;
        }

        public bool Has(RuntimeFeature feature)
            => (Runtime & feature) == feature;
    }
}
