using System;

namespace MonoMod.Utils {
    /// <summary>
    /// Generic platform enum.
    /// </summary>
    [Flags]
    [MonoMod__OldName__("MonoMod.Helpers.Platform")]
    public enum Platform : int {
        OS = 1 << 0, // Applied to all OSes (Unknown, Windows, MacOS, ...)

        Bits32 = 0,
        Bits64 = 1 << 1,

        NT = 1 << 2,
        Unix = 1 << 3,

        ARM = 1 << 16,

        Unknown = OS | (1 << 4),
        Windows = OS | NT | (1 << 5),
        MacOS = OS | Unix | (1 << 6),
        Linux = OS | Unix | (1 << 7),
        Android = Linux | (1 << 8),
        iOS = MacOS | (1 << 9), // Darwin shared across macOS and iOS
    }
}
