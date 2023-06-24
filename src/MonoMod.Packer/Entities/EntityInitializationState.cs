using System;

#pragma warning disable CA1069 // Enums values should not be duplicated

namespace MonoMod.Packer.Entities {
    [Flags]
    internal enum EntityInitializationState {
        None = 0,

        // Types
        TypeMergeMode = 1 << 0,
        HasUnifiableBase = 1 << 1,
        BaseType = 1 << 2,

        // Fields
        HasInitializer = 1 << 0,
        Initializer = 1 << 1,
    }
}
