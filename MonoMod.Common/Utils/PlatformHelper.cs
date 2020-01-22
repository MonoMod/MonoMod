using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonoMod.Utils {
#if !MONOMOD_INTERNAL
    public
#endif
    static class PlatformHelper {

        static PlatformHelper() {
            Current = Platform.Unknown;

#if NETSTANDARD
            // RuntimeInformation.IsOSPlatform is lying: https://github.com/dotnet/corefx/issues/3032
            // Determine the platform based on the path.
            string windir = Environment.GetEnvironmentVariable("windir");
            if (!string.IsNullOrEmpty(windir) && windir.Contains(@"\") && Directory.Exists(windir)) {
                Current = Platform.Windows;

            } else if (File.Exists("/proc/sys/kernel/ostype")) {
                string osType = File.ReadAllText("/proc/sys/kernel/ostype");
                if (osType.StartsWith("Linux", StringComparison.OrdinalIgnoreCase)) {
                    Current = Platform.Linux;
                } else {
                    Current = Platform.Unix;
                }

            } else if (File.Exists("/System/Library/CoreServices/SystemVersion.plist")) {
                Current = Platform.MacOS;
            }

#else
            // For old Mono, get from a private property to accurately get the platform.
            // static extern PlatformID Platform
            PropertyInfo p_Platform = typeof(Environment).GetProperty("Platform", BindingFlags.NonPublic | BindingFlags.Static);
            string platID;
            if (p_Platform != null) {
                platID = p_Platform.GetValue(null, new object[0]).ToString();
            } else {
                // For .NET and newer Mono, use the usual value.
                platID = Environment.OSVersion.Platform.ToString();
            }
            platID = platID.ToLowerInvariant();

            if (platID.Contains("win")) {
                Current = Platform.Windows;
            } else if (platID.Contains("mac") || platID.Contains("osx")) {
                Current = Platform.MacOS;
            } else if (platID.Contains("lin") || platID.Contains("unix")) {
                Current = Platform.Linux;
            }
#endif

            if (Is(Platform.Linux) && Directory.Exists("/data") && File.Exists("/system/build.prop")) {
                Current = Platform.Android;
            } else if (Is(Platform.Unix) && Directory.Exists("/Applications") && Directory.Exists("/System")) {
                Current = Platform.iOS;
            }

            // Is64BitOperatingSystem has been added in .NET Framework 4.0
            MethodInfo m_get_Is64BitOperatingSystem = typeof(Environment).GetProperty("Is64BitOperatingSystem")?.GetGetMethod();
            if (m_get_Is64BitOperatingSystem != null)
                Current |= (((bool) m_get_Is64BitOperatingSystem.Invoke(null, new object[0])) ? Platform.Bits64 : 0);
            else
                Current |= (IntPtr.Size >= 8 ? Platform.Bits64 : 0);

#if NETSTANDARD
            // Detect ARM based on RuntimeInformation.
            if (RuntimeInformation.ProcessArchitecture.HasFlag(Architecture.Arm) ||
                RuntimeInformation.OSArchitecture.HasFlag(Architecture.Arm))
                Current |= Platform.ARM;
#else
            if ((Is(Platform.Unix) || Is(Platform.Unknown)) && Type.GetType("Mono.Runtime") != null) {
                /* I'd love to use RuntimeInformation, but it returns X64 up until...
                 * https://github.com/mono/mono/commit/396559769d0e4ca72837e44bcf837b7c91596414
                 * ... and that commit still hasn't reached Mono 5.16 on Debian, dated
                 * tarball Mon Nov 26 17:21:35 UTC 2018
                 * There's also the possibility to [DllImport("libc.so.6")]
                 * -ade
                 */
                try {
                    string arch;
                    using (Process uname = Process.Start(new ProcessStartInfo("uname", "-m") {
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    })) {
                        arch = uname.StandardOutput.ReadLine().Trim();
                    }

                    if (arch.StartsWith("aarch") || arch.StartsWith("arm"))
                        Current |= Platform.ARM;
                } catch (Exception) {
                    // Starting a process can fail for various reasons. One of them being...
                    /* System.MissingMethodException: Method 'MonoIO.CreatePipe' not found.
                     * at System.Diagnostics.Process.StartWithCreateProcess (System.Diagnostics.ProcessStartInfo startInfo) <0x414ceb20 + 0x0061f> in <filename unknown>:0 
                     */
                }

            } else {
                // Detect ARM based on PE info or uname.
                typeof(object).Module.GetPEKind(out PortableExecutableKinds peKind, out ImageFileMachine machine);
                if (machine == (ImageFileMachine) 0x01C4 /* ARM, .NET Framework 4.5 */)
                    Current |= Platform.ARM;
            }
#endif

            LibrarySuffix =
                Is(Platform.MacOS) ? "dylib" :
                Is(Platform.Unix) ? "so" :
                "dll";

        }

        public static Platform Current { get; private set; }
        public static string LibrarySuffix { get; private set; }

        public static bool Is(Platform platform)
            => (Current & platform) == platform;

    }
}
