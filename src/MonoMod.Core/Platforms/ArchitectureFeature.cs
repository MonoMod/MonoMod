using System;

namespace MonoMod.Core.Platforms {
    [Flags]
    public enum ArchitectureFeature {
        None,

        FixedInstructionSize = 0x01,
        Immediate64 = 0x02,
        CreateAltEntryPoint = 0x04,
    }
}
