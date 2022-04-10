namespace MonoMod.Core.Utils {
    public enum ArchitectureKind {
        Unknown,
        Bits64 = 1,
        x86 = 0x01 << 1,
        x86_64 = x86 | Bits64,
        Arm = 0x02 << 1,
        Arm64 = Arm | Bits64,
    }
}
