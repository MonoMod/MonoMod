using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Utils
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "x86_64 is the name of the architecture, at least for Intel. AMD64 is another reasonable name.")]
    public enum ArchitectureKind
    {
        Unknown,
        Bits64 = 1,
        x86 = 0x01 << 1,
        x86_64 = x86 | Bits64,
        Arm = 0x02 << 1,
        Arm64 = Arm | Bits64,
    }
}
