using System;

#pragma warning disable CA1069 // Enums values should not be duplicated

namespace MonoMod.Packer.Entities {
    [Flags]
    internal enum EntityFlags {
        None = 0,

        // Types
        HasUnifiableBase = 1 << 0,


        // Fields
        HasUnifiableInitializer = 1 << 0,

    }
}
