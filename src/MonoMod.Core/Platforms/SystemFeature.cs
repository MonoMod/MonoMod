using System;

namespace MonoMod.Core.Platforms {
    [Flags]
    public enum SystemFeature {
        None,

        RWXPages = 0x01,
        RXPages = 0x02,
    }
}
