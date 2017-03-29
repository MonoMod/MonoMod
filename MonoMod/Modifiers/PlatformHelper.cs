using System;
using System.IO;
using System.Reflection;

public static class PlatformHelper {

    static PlatformHelper() {
        //for mono, get from
        //static extern Platf<ormID Platform
        PropertyInfo property_platform = typeof(Environment).GetProperty("Platform", BindingFlags.NonPublic | BindingFlags.Static);
        string platID;
        if (property_platform != null) {
            platID = property_platform.GetValue(null, new object[0]).ToString();
        } else {
            //for .net, use default value
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

}
