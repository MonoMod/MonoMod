using MonoMod.Backports;
using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.Core.Platforms {
    public static class Platform {
        public static IRuntime CreateCurrentRuntime()
            => PlatformDetection.Runtime switch {
                RuntimeKind.Framework => Runtimes.FxBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.CoreCLR => Runtimes.CoreCLRBaseRuntime.CreateForVersion(PlatformDetection.RuntimeVersion),
                RuntimeKind.Mono => throw new NotImplementedException(),
                var kind => throw new PlatformNotSupportedException($"Platform kind {kind} not supported"),
            };

        public static IArchitecture CreateCurrentArchitecture()
            => throw new NotImplementedException();

        public static ISystem CreateCurrentSystem()
            => throw new NotImplementedException();
    }
}
