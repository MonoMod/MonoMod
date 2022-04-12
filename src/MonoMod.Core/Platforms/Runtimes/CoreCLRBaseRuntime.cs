using System;

namespace MonoMod.Core.Platforms.Runtimes {
    internal abstract class CoreCLRBaseRuntime : FxCoreCLRBaseRuntime {

        public static CoreCLRBaseRuntime CreateForVersion(Version version) {

            switch (version.Major) {
                case 2:
                case 4:
                    // .NET Core 2.1 (it actually only seems to give a major version of 4, but 2 is here for safety)
                    // TODO:
                    break;

                case 3:
                    // .NET Core 3.x
                    // TODO:
                    break;

                case 5:
                    // .NET 5.0.x
                    // TODO:
                    break;

                case 6:
                    // .NET 6.0.x
                    // TODO:
                    break;

                case 7:
                    // .NET 7.0.x
                    // TODO:
                    break;

                // currently, we need to manually add support for new versions.
                // TODO: possibly fall back to a JIT GUID check if we can?

                default: throw new PlatformNotSupportedException($"CoreCLR version {version} is not supported");
            }

            throw new NotImplementedException();
        }

    }
}
