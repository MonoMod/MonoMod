using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms.Runtimes {
    internal abstract class CoreBaseRuntime : FxCoreBaseRuntime {

        public override RuntimeKind Target => RuntimeKind.CoreCLR;

        public static CoreBaseRuntime CreateForVersion(Version version) {

            switch (version.Major) {
                case 2:
                case 4:
                    // .NET Core 2.1 (it actually only seems to give a major version of 4, but 2 is here for safety)
                    // TODO:
                    break;

                case 3:
                    // .NET Core 3.x
                    return version.Minor switch {
                        0 => new Core30Runtime(),
                        1 => new Core31Runtime(),
                        _ => throw new PlatformNotSupportedException($"Unknown .NET Core 3.x minor version {version.Minor}"),
                    };

                case 5:
                    // .NET 5.0.x
                    return new Core50Runtime();

                case 6:
                    // .NET 6.0.x
                    return new Core60Runtime();

                /*
                case 7:
                    // .NET 7.0.x
                    // TODO:
                    break;
                */

                // currently, we need to manually add support for new versions.
                // TODO: possibly fall back to a JIT GUID check if we can?

                default: throw new PlatformNotSupportedException($"CoreCLR version {version} is not supported");
            }

            throw new NotImplementedException();
        }

    }
}
