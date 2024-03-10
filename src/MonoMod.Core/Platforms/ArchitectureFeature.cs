using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A set of features which may be provided by an <see cref="IArchitecture"/> implementation.
    /// </summary>
    [Flags]
    public enum ArchitectureFeature
    {
        /// <summary>
        /// No features are implemented.
        /// </summary>
        None,

        /// <summary>
        /// The architecture has a fixed instruction size.
        /// </summary>
        FixedInstructionSize = 0x01,
        /// <summary>
        /// The architecture has the ability to encode 64-bit immediate values.
        /// </summary>
        Immediate64 = 0x02,
        /// <summary>
        /// The architecture implements <see cref="IArchitecture.AltEntryFactory"/> to allow the creation
        /// of alternate entry points.
        /// </summary>
        CreateAltEntryPoint = 0x04,
    }
}
