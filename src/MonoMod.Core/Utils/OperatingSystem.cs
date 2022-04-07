namespace MonoMod.Core.Utils {
    public enum OperatingSystem {
        Unknown = 0,

        // low 5 bits are flags for the base OS
        // bit 0 is Posix, 1 is Windows, 2 is OSX, 3 is Linux, 4 is BSD
        // remaining bits are a subtype

        Posix = 1 << 0,

        Linux = 1 << 3 | Posix,
        Android = 0x01 << 5 | Linux, // Android is a subset of Linux
        OSX = 1 << 2 | Posix,
        IOS = 0x01 << 5 | OSX, // iOS is a subset of OSX
        BSD = 1 << 4 | Posix,

        Windows = 1 << 1,
        Wine = 0x01 << 5 | Windows,
    }

    public static class OperatingSystemExtensions {
        public static bool Is(this OperatingSystem operatingSystem, OperatingSystem test) => operatingSystem.Has(test);
        public static OperatingSystem GetKernel(this OperatingSystem operatingSystem) => (OperatingSystem) ((int) operatingSystem & 0b11111);
        public static int GetSubtypeId(this OperatingSystem operatingSystem) => (int) operatingSystem >> 5;
    }
}
