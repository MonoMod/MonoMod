using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core {
    [Flags]
    public enum SystemFeature {
        None,

        RWXPages = 0x01,
        RXPages = 0x02,
    }
}
