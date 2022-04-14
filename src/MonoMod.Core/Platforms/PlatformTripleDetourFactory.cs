using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms {

    public class PlatformTripleDetourFactory : IDetourFactory {

        private readonly PlatformTriple triple;

        public PlatformTripleDetourFactory(PlatformTriple triple) {
            this.triple = triple;
        }

        public FeatureFlags SupportedFeatures => triple.SupportedFeatures;

        public ICoreDetour CreateDetour(MethodBase source, MethodBase dest) {
            throw new NotImplementedException();
        }
    }
}
