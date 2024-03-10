using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.Utils
{
    public static class PlatformDetection
    {
        #region OS/Arch
        private static int platInitState;
        private static OSKind os;
        private static ArchitectureKind arch;

        private static void EnsurePlatformInfoInitialized()
        {
            if (platInitState != 0)
            {
                return;
            }

            // we're actually OK with invoking this multiple times on different threads, because it
            // *should* give the same results each time.
            var detected = DetectPlatformInfo();
            os = detected.OS;
            arch = detected.Arch;
            Thread.MemoryBarrier();
            _ = Interlocked.Exchange(ref platInitState, 1);
        }

        public static OSKind OS
        {
            get
            {
                EnsurePlatformInfoInitialized();
                return os;
            }
        }

        public static ArchitectureKind Architecture
        {
            get
            {
                EnsurePlatformInfoInitialized();
                return arch;
            }
        }

        private static (OSKind OS, ArchitectureKind Arch) DetectPlatformInfo()
        {
            var os = OSKind.Unknown;
            var arch = ArchitectureKind.Unknown;

            {
                // For old Mono, get from a private property to accurately get the platform.
                // static extern PlatformID Platform
                var p_Platform = typeof(Environment).GetProperty("Platform", BindingFlags.NonPublic | BindingFlags.Static);
                string? platID;
                if (p_Platform != null)
                {
                    platID = p_Platform.GetValue(null, null)?.ToString();
                }
                else
                {
                    // For .NET and newer Mono, use the usual value.
                    platID = Environment.OSVersion.Platform.ToString();
                }
                platID = platID?.ToUpperInvariant() ?? "";

                if (platID.Contains("WIN", StringComparison.Ordinal))
                {
                    os = OSKind.Windows;
                }
                else if (platID.Contains("MAC", StringComparison.Ordinal) || platID.Contains("OSX", StringComparison.Ordinal))
                {
                    os = OSKind.OSX;
                }
                else if (platID.Contains("LIN", StringComparison.Ordinal))
                {
                    os = OSKind.Linux;
                }
                else if (platID.Contains("BSD", StringComparison.Ordinal))
                {
                    os = OSKind.BSD;
                }
                else if (platID.Contains("UNIX", StringComparison.Ordinal))
                {
                    os = OSKind.Posix;
                }
            }

            // Try to use OS-specific methods of determining OS/Arch info
            if (os == OSKind.Windows)
            {
                DetectInfoWindows(ref os, ref arch);
            }
            else if ((os & OSKind.Posix) != 0)
            {
                DetectInfoPosix(ref os, ref arch);
            }

            if (os == OSKind.Unknown)
            {
                // Welp.

            }
            else if (os == OSKind.Linux &&
                Directory.Exists("/data") && File.Exists("/system/build.prop")
            )
            {
                os = OSKind.Android;
            }
            else if (os == OSKind.Posix &&
                Directory.Exists("/Applications") && Directory.Exists("/System") &&
                Directory.Exists("/User") && !Directory.Exists("/Users")
            )
            {
                os = OSKind.IOS;
            }
            else if (os == OSKind.Windows &&
                CheckWine()
            )
            {
                // Sorry, Wine devs, but you might want to look at DetourRuntimeNETPlatform.
                os = OSKind.Wine;
            }

            MMDbgLog.Info($"Platform info: {os} {arch}");
            return (os, arch);
        }


        #region OS-specific arch detection


        private static unsafe int PosixUname(OSKind os, byte* buf)
        {
            static int Libc(byte* buf) => Interop.Unix.Uname(buf);
            static int Osx(byte* buf) => Interop.OSX.Uname(buf);
            return os == OSKind.OSX ? Osx(buf) : Libc(buf);
        }

        private static unsafe string GetCString(ReadOnlySpan<byte> buffer, out int nullByte)
        {
            fixed (byte* buf = buffer)
            {
                return Marshal.PtrToStringAnsi((IntPtr)buf, nullByte = buffer.IndexOf((byte)0));
            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "This method failing to detect information should not be a hard error. Exceptions thrown because of " +
            "issues with P/Invoke and the like should not prevent the OS and arch info from being populated.")]
        private static void DetectInfoPosix(ref OSKind os, ref ArchitectureKind arch)
        {
            try
            {
                // we want to call libc's uname() function
                // the fields we're interested in are sysname and machine, which are field 0 and 4 respectively.

                // Unfortunately for us, the size of the utsname struct depends heavily on the platform. Fortunately for us,
                // the returned data is all null-terminated strings. Hopefully, the unused data in the fields are filled with
                // zeroes or untouched, which will allow us to easily scan for the strings.
                // This last condition is *not* always true on Linux, depending on how the hostname is set.
                // See https://github.com/tModLoader/tModLoader/issues/3766

                // Because the amount of space required for this syscall is unknown, we'll just allocate 6*513 bytes for it, and scan.

                Span<byte> buffer = new byte[6 * 513];
                unsafe
                {
                    fixed (byte* bufPtr = buffer)
                    {
                        if (PosixUname(os, bufPtr) < 0)
                        {
                            // uh-oh, uname failed. Log the error if we can  get it and return normally.
                            var msg = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                            MMDbgLog.Error($"uname() syscall failed! {msg}");
                            return;
                        }
                    }
                }

                // buffer now contains a bunch of null-terminated strings
                // the first of these is the kernel name

                var kernelName = GetCString(buffer, out var nullByteOffs).ToUpperInvariant();
                buffer = buffer.Slice(nullByteOffs);

                MMDbgLog.Trace($"uname() call returned {kernelName}");

                // now we want to inspect the fields and select something useful from them
                if (kernelName.Contains("LINUX", StringComparison.Ordinal))
                { // A Linux kernel
                    os = OSKind.Linux;
                }
                else if (kernelName.Contains("DARWIN", StringComparison.Ordinal))
                { // the MacOS kernel
                    os = OSKind.OSX;
                }
                else if (kernelName.Contains("BSD", StringComparison.Ordinal))
                { // a BSD kernel
                    // Note: I'm fairly sure that the different BSDs vary quite a lot, so it may be worth checking with more specificity here
                    os = OSKind.BSD;
                }
                // TODO: fill in other known kernel names

                var machineName = GetMachineNamePosix(os, buffer).ToUpperInvariant();

                if (machineName.Contains("X86_64", StringComparison.Ordinal))
                {
                    arch = ArchitectureKind.x86_64;
                }
                else if (machineName.Contains("AMD64", StringComparison.Ordinal))
                {
                    arch = ArchitectureKind.x86_64;
                }
                else if (machineName.Contains("X86", StringComparison.Ordinal))
                {
                    arch = ArchitectureKind.x86;
                }
                else if (machineName.Contains("AARCH64", StringComparison.Ordinal))
                {
                    arch = ArchitectureKind.Arm64;
                }
                else if (machineName.Contains("ARM64", StringComparison.Ordinal))
                {
                    arch = ArchitectureKind.Arm64;
                }
                else if (machineName.Contains("ARM", StringComparison.Ordinal))
                {
                    arch = ArchitectureKind.Arm;
                }
                // TODO: fill in other values for machine

                MMDbgLog.Trace($"uname() detected architecture info: {os} {arch}");
            }
            catch (Exception e)
            {
                MMDbgLog.Error($"Error trying to detect info on POSIX-like system {e}");
                return;
            }
        }

        private static unsafe string GetMachineNamePosix(OSKind os, Span<byte> unameBuffer)
        {
            string? machineName = null;

            if (os == OSKind.Linux)
            {
                // we know the kernel is linux
                // first, lets try go get libc!getauxval and use that

                var libc = DynDll.OpenLibrary(Interop.Unix.LibC);
                if (DynDll.TryGetExport(libc, "getauxval", out var getAuxVal))
                {
                    var result = ((delegate* unmanaged[Cdecl]<nint, nint>)getAuxVal)(Interop.Unix.AT_PLATFORM);
                    if (result is not 0)
                    {
                        machineName = Marshal.PtrToStringAnsi(result);
                        MMDbgLog.Trace($"Got architecture from getauxval(): {machineName}");
                    }
                }

                if (machineName is null)
                {
                    // try to use /proc/self/auxv (present since Linux 2.6.0, which released in 2004)
                    // sometimes it's not accessible (no idea why, but it is), we should handle that
                    try
                    {
                        var auxvBytes = Helpers.ReadAllBytes("/proc/self/auxv").AsSpan();
                        var auxv = MemoryMarshal.Cast<byte, Interop.Unix.LinuxAuxvEntry>(auxvBytes);
                        machineName = string.Empty;
                        foreach (var entry in auxv)
                        {
                            if (entry.Key != Interop.Unix.AT_PLATFORM)
                            {
                                continue;
                            }

                            machineName = Marshal.PtrToStringAnsi(entry.Value) ?? string.Empty;
                            break;
                        }

                        if (machineName.Length == 0)
                        {
                            MMDbgLog.Warning($"Auxv table did not inlcude useful AT_PLATFORM (0x{Interop.Unix.AT_PLATFORM:x}) entry");
                            foreach (var entry in auxv)
                            {
                                MMDbgLog.Trace($"{entry.Key:x16} = {entry.Value:x16}");
                            }
                            machineName = null;
                        }
                        else
                        {
                            MMDbgLog.Trace($"Got architecture name {machineName} from /proc/self/auxv");
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        MMDbgLog.Warning("Could not read /proc/self/auxv, and libc does not have getauxval");
                        MMDbgLog.Warning("Falling back to parsing out of uname() result...");
                        MMDbgLog.Warning($"{ex}");
                    }
                }
            }

            // fall back to trying to pull from uname, however well that will work...
            if (machineName is null)
            {
                int nullByteOffs;
                // this is a non-unix kernel, or a fallback, lets hope their uname buffers are more well maintained...

                // struct utsname
                // 0:   char sysname[];    // Operating system name (e.g., "Linux") 
                // 1:   char nodename[];   // Name within "some implementation-defined network" 
                // 2:   char release[];    // Operating system release (e.g., "2.6.28")
                // 3:   char version[];    // Operating system version
                // 4:   char machine[];    // Hardware identifier
                // we've already skipped 0: sysname

                for (var i = 0; i < 4; i++)
                { // we want to jump to string 4, but we've already skipped the text of the first
                    if (i != 0)
                    {
                        // skip a string
                        nullByteOffs = unameBuffer.IndexOf((byte)0);
                        unameBuffer = unameBuffer.Slice(nullByteOffs);

                        if (i == 1)
                        {
                            // we just read nodename
                            // if the nodename is less than 4 bytes, then it's likely to have only 1 null byte, then some more
                            // non-null characters before the final padding of the field
                            // we want to try to detect and correct for that
                            if (nullByteOffs < 5 && unameBuffer.Length >= 2 && unameBuffer[1] != 0)
                            {
                                // we want to skip a bit more
                                // note: it is possible for this to fill the entire buffer, and still cause problems, but standard
                                // configurations don't have this issue, so we expect it to be rare enough to not worry about. There's
                                // not really anything we can do anyway.
                                nullByteOffs = unameBuffer.Slice(1).IndexOf((byte)0);
                                unameBuffer = unameBuffer.Slice(nullByteOffs + 1);
                            }
                        }
                    }
                    // then advance to the next one
                    var j = 0;
                    for (; j < unameBuffer.Length && unameBuffer[j] == 0; j++) { }
                    unameBuffer = unameBuffer.Slice(j);
                }

                // and here we find the machine field
                machineName = GetCString(unameBuffer, out _);
                MMDbgLog.Trace($"Got architecture name {machineName} from uname()");
            }

            return machineName;
        }

        private static unsafe void DetectInfoWindows(ref OSKind os, ref ArchitectureKind arch)
        {
            Interop.Windows.SYSTEM_INFO sysInfo;
            Interop.Windows.GetSystemInfo(&sysInfo);

            // we don't update OS here, because Windows

            // https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-system_info
            arch = sysInfo.Anonymous.Anonymous.wProcessorArchitecture switch
            {
                Interop.Windows.PROCESSOR_ARCHITECTURE_AMD64 => ArchitectureKind.x86_64,
                Interop.Windows.PROCESSOR_ARCHITECTURE_IA64 => throw new PlatformNotSupportedException("You're running .NET on an Itanium device!?!?"),
                Interop.Windows.PROCESSOR_ARCHITECTURE_INTEL => ArchitectureKind.x86,
                Interop.Windows.PROCESSOR_ARCHITECTURE_ARM => ArchitectureKind.Arm,
                Interop.Windows.PROCESSOR_ARCHITECTURE_ARM64 => ArchitectureKind.Arm64,
                var x => throw new PlatformNotSupportedException($"Unknown Windows processor architecture {x}"),
            };
        }
        #endregion

        // Separated method so that this P/Invoke mess doesn't error out on non-Windows.
        private static unsafe bool CheckWine()
        {
            // wine_get_version can be missing because of course it can.
            // Read a configuration switch.
            if (Switches.TryGetSwitchEnabled(Switches.RunningOnWine, out var runningWine))
                return runningWine;

            // The "Dalamud" plugin loader for FFXIV uses Harmony, coreclr and wine. What a nice combo!
            // At least they went ahead and provide an environment variable for everyone to check.
            // See https://github.com/goatcorp/FFXIVQuickLauncher/blob/8685db4a0e8ec53235fb08cd88aded7c7061d9fb/src/XIVLauncher/Settings/EnvironmentSettings.cs
            var env = Environment.GetEnvironmentVariable("XL_WINEONLINUX")?.ToUpperInvariant();
            if (env == "TRUE")
                return true;
            if (env == "FALSE")
                return false;

            fixed (char* pNtdll = "ntdll.dll")
            {
                var ntdll = Interop.Windows.GetModuleHandleW((ushort*)pNtdll);
                if (ntdll != Interop.Windows.HMODULE.NULL && ntdll != Interop.Windows.HMODULE.INVALID_VALUE)
                {
                    fixed (byte* pWineGetVersion = "wineGetVersion"u8)
                    {
                        if (Interop.Windows.GetProcAddress(ntdll, (sbyte*)pWineGetVersion) != IntPtr.Zero)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        #endregion

        #region Runtime
        private static int runtimeInitState;
        private static RuntimeKind runtime;
        private static Version? runtimeVersion;

        [MemberNotNull(nameof(runtimeVersion))]
        private static void EnsureRuntimeInitialized()
        {
            if (runtimeInitState != 0)
            {
                if (runtimeVersion is null)
                {
                    throw new InvalidOperationException("Despite runtimeInitState being set, runtimeVersion was somehow null");
                }
                return;
            }

            var runtimeInfo = DetermineRuntimeInfo();
            runtime = runtimeInfo.Rt;
            runtimeVersion = runtimeInfo.Ver;

            Thread.MemoryBarrier();
            _ = Interlocked.Exchange(ref runtimeInitState, 1);
        }

        public static RuntimeKind Runtime
        {
            get
            {
                EnsureRuntimeInitialized();
                return runtime;
            }
        }

        public static Version RuntimeVersion
        {
            get
            {
                EnsureRuntimeInitialized();
                return runtimeVersion;
            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "In old versions of Framework, there is no Version.TryParse, and so we must call the constructor " +
            "and catch any exception that may ocurr.")]
        private static (RuntimeKind Rt, Version Ver) DetermineRuntimeInfo()
        {
            RuntimeKind runtime;
            Version? version = null; // an unknown version

            var isMono =
                // This is what everyone expects.
                Type.GetType("Mono.Runtime") != null ||
                // .NET Core BCL running on Mono, see https://github.com/dotnet/runtime/blob/main/src/libraries/Common/tests/TestUtilities/System/PlatformDetection.cs
                Type.GetType("Mono.RuntimeStructs") != null;

            var isCoreBcl = typeof(object).Assembly.GetName().Name == "System.Private.CoreLib";

            if (isMono)
            {
                runtime = RuntimeKind.Mono;
            }
            else if (isCoreBcl && !isMono)
            {
                runtime = RuntimeKind.CoreCLR;
            }
            else
            {
                runtime = RuntimeKind.Framework;
            }

            MMDbgLog.Trace($"IsMono: {isMono}, IsCoreBcl: {isCoreBcl}");

            var sysVer = Environment.Version;
            MMDbgLog.Trace($"Returned system version: {sysVer}");

            // RuntimeInformation is present in FX 4.7.1+ and all netstandard and Core releases, however its location varies
            // In FX, it is in mscorlib
            var rti = Type.GetType("System.Runtime.InteropServices.RuntimeInformation");
            // however, in Core, its in System.Runtime.InteropServices.RuntimeInformation
            rti ??= Type.GetType("System.Runtime.InteropServices.RuntimeInformation, System.Runtime.InteropServices.RuntimeInformation");

            // FrameworkDescription is a string which (is supposed to) describe the runtime
            var fxDesc = (string?)rti?.GetProperty("FrameworkDescription")?.GetValue(null, null);
            MMDbgLog.Trace($"FrameworkDescription: {fxDesc ?? "(null)"}");

            if (fxDesc is not null)
            {
                // Example values:
                // '.NET Framework 4.7.2'
                // '.NET 6.0.9'
                // '.NET 6.0.0-rtm.21522.10'

                // If we could get FrameworkDescription, we want to check the start of it for each known runtime
                const string MonoPrefix = "Mono ";
                const string NetCore = ".NET Core ";
                const string NetFramework = ".NET Framework ";
                const string Net5Plus = ".NET ";

                int prefixLength;
                if (fxDesc.StartsWith(MonoPrefix, StringComparison.Ordinal))
                {
                    runtime = RuntimeKind.Mono;
                    prefixLength = MonoPrefix.Length;
                }
                else if (fxDesc.StartsWith(NetCore, StringComparison.Ordinal))
                {
                    runtime = RuntimeKind.CoreCLR;
                    prefixLength = NetCore.Length;
                }
                else if (fxDesc.StartsWith(NetFramework, StringComparison.Ordinal))
                {
                    runtime = RuntimeKind.Framework;
                    prefixLength = NetFramework.Length;
                }
                else if (fxDesc.StartsWith(Net5Plus, StringComparison.Ordinal))
                {
                    runtime = RuntimeKind.CoreCLR;
                    prefixLength = Net5Plus.Length;
                }
                else
                {
                    runtime = RuntimeKind.Unknown; // even if we think we already know, if we get to this point, explicitly set to unknown
                    // this *likely* means that this is some new/obscure runtime
                    prefixLength = fxDesc.Length;
                }

                // find the next dash or space, if any, because everything up to that should be the version
                var space = fxDesc.IndexOfAny([' ', '-'], prefixLength);
                if (space < 0)
                    space = fxDesc.Length;

                var versionString = fxDesc.Substring(prefixLength, space - prefixLength);

                try
                {
                    version = new Version(versionString);
                }
                catch (Exception e)
                {
                    MMDbgLog.Error($"Invalid version string pulled from FrameworkDescription ('{fxDesc}') {e}");
                }

                // TODO: map .NET Core 2.1 version to something saner
            }

            // only on old Framework is this anything *close* to reliable
            if (runtime == RuntimeKind.Framework)
                version ??= sysVer;

            // TODO: map strange (read: Framework) versions correctly

            MMDbgLog.Info($"Detected runtime: {runtime} {version?.ToString() ?? "(null)"}");

            return (runtime, version ?? new Version(0, 0));
        }

        #endregion
    }
}
