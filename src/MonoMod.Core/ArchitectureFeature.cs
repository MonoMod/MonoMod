using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core {
    [Flags]
    public enum ArchitectureFeature {
        None,

        FixedInstructionSize = 0x01,
        Immediate64 = 0x02,

    }
}
