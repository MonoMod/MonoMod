using MonoMod.Utils;
using System;
using Xunit;

namespace MonoMod.UnitTest {
    public class PlatformFactAttribute : FactAttribute {
        public PlatformFactAttribute(params string[] names) {
            bool? matchPlat = null;
            bool? matchRuntime = null;

            foreach (string name in names) {
                if (Enum.TryParse(name, out Platform plat)) {
                    matchPlat = PlatformHelper.Is(plat) ? true : (matchPlat ?? false);

                } else if (Enum.TryParse(name, out Runtime runtime)) {
                    switch (runtime) {
#if NETFRAMEWORK
                        case Runtime.FX:
                            matchRuntime = !ReflectionHelper.IsMono || (matchRuntime ?? false);
                            break;
                        case Runtime.Mono:
                            matchRuntime = ReflectionHelper.IsMono || (matchRuntime ?? false);
                            break;

#else
                        case Runtime.Core:
                            matchRuntime = true;
                            break;
#endif

                        default:
                            matchRuntime = matchRuntime ?? false;
                            break;
                    }
                }
            }

            if (!(matchPlat ?? true)) {
                Skip = "Platform doesn't match";
                return;
            }

            if (!(matchRuntime ?? true)) {
                Skip = "Runtime doesn't match";
                return;
            }
        }

        private enum Runtime {
            FX,
            Framework = FX,
            Core,
            Mono
        }
    }
}
