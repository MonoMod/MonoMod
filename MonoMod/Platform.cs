/// <summary>
/// Generic platform enum.
/// </summary>
public enum Platform : int {
    None = 0,

    // Underlying platform categories
    OS = 1,

    X86 = 0,
    X64 = 2,

    NT = 4,
    Unix = 8,

    // Operating systems (OSes are always "and-equal" to OS)
    Unknown = OS | 16,
    Windows = OS | NT   |   32,
    MacOS =   OS | Unix |   64,
    Linux =   OS | Unix |  128,
    Android = Linux     |  256,
    iOS =     MacOS     |  512, // Darwin

    // AMD64 (64bit) variants (always "and-equal" to X64)
    Unknown64 = Unknown | X64,
    Windows64 = Windows | X64,
    MacOS64 =   MacOS   | X64,
    Linux64 =   Linux   | X64,
    Android64 = Android | X64,
    iOS64 =     iOS     | X64
}
