using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core.Platforms {
    public class HostTripleDetourFactory : IDetourFactory {

        public IArchitecture Architecture { get; }
        public ISystem System { get; }
        public IRuntime Runtime { get; }

        public HostTripleDetourFactory(IArchitecture architecture, ISystem system, IRuntime runtime) {
            Architecture = architecture;
            System = system;
            Runtime = runtime;
        }

        public (Architecture Arch, OSKind OS, Runtime Runtime) HostTriple => (Architecture.Target, System.Target, Runtime.Target);

        public FeatureFlags SupportedFeatures => new(Architecture.Features, System.Features, Runtime.Features);

        public ICoreDetour CreateDetour(MethodBase source, MethodBase dest) {
            throw new NotImplementedException();
        }
    }
}
