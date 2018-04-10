using System;
using System.IO;
using System.Reflection;

namespace MonoMod.Utils {
    [MonoMod__OldName__("MonoMod.Helpers.PlatformHelper")]
    public static class PlatformHelper {

        static PlatformHelper() {
            // For old Mono, get from a private property to accurately get the platform.
            // static extern PlatformID Platform
            PropertyInfo property_platform = typeof(Environment).GetProperty("Platform", BindingFlags.NonPublic | BindingFlags.Static);
            string platID;
            if (property_platform != null) {
                platID = property_platform.GetValue(null, new object[0]).ToString();
            } else {
                // For .NET and newer Mono, use the usual value.
                platID = Environment.OSVersion.Platform.ToString();
            }
            platID = platID.ToLowerInvariant();

            Current = Platform.Unknown;
            if (platID.Contains("win")) {
                Current = Platform.Windows;
            } else if (platID.Contains("mac") || platID.Contains("osx")) {
                Current = Platform.MacOS;
            } else if (platID.Contains("lin") || platID.Contains("unix")) {
                Current = Platform.Linux;
            }

            if (Directory.Exists("/data") && File.Exists("/system/build.prop")) {
                Current = Platform.Android;
            } else if (Directory.Exists("/Applications") && Directory.Exists("/System")) {
                Current = Platform.iOS;
            }

            Current |= (IntPtr.Size == 4 ? Platform.X86 : Platform.X64);
        }

        public static Platform Current { get; private set; }

        public static bool Is(Platform platform)
            => (Current & platform) == platform;

    }
}
