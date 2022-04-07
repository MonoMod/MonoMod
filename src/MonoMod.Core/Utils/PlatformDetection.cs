using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Utils {
    public static class PlatformDetection {
        public enum OSKind {
            Unknown = 0,

            Posix = 0x01,
            Linux = 0x01 << 1 | Posix,
            Android = 0x05 << 1 | Posix, // Android is a subset of Linux
            OSX = 0x02 << 1 | Posix,
            IOS = 0x03 << 1 | Posix, // iOS is a subset of OSX
            BSD = 0x04 << 1 | Posix,

            Windows = 0x10 << 1,
            Wine = 0x11 << 1,
        }

        public enum Arch {
            Unknown,
            x86,
            x86_64,
            Arm,
            Arm64
        }

        public static (OSKind, Arch) DetectPlatformInfo() {
            OSKind os = OSKind.Unknown;
            Arch arch = Arch.Unknown;

            {
                // For old Mono, get from a private property to accurately get the platform.
                // static extern PlatformID Platform
                PropertyInfo? p_Platform = typeof(Environment).GetProperty("Platform", BindingFlags.NonPublic | BindingFlags.Static);
                string? platID;
                if (p_Platform != null) {
                    platID = p_Platform.GetValue(null, new object[0])?.ToString();
                } else {
                    // For .NET and newer Mono, use the usual value.
                    platID = Environment.OSVersion.Platform.ToString();
                }
                platID = platID?.ToUpperInvariant() ?? "";

                if (platID.Contains("WIN")) {
                    os = OSKind.Windows;
                } else if (platID.Contains("MAC") || platID.Contains("OSX")) {
                    os = OSKind.OSX;
                } else if (platID.Contains("LIN")) {
                    os = OSKind.Linux;
                } else if (platID.Contains("BSD")) {
                    os = OSKind.BSD;
                } else if (platID.Contains("UNIX")) {
                    os = OSKind.Posix;
                }
            }

            // Try to use OS-specific methods of determining OS/Arch info
            if (os == OSKind.Windows) {
                DetectInfoWindows(ref os, ref arch);
            } else if ((os & OSKind.Posix) != 0) {
                DetectInfoPosix(ref os, ref arch);
            }

            if (os == OSKind.Unknown) {
                // Welp.

            } else if (os == OSKind.Linux &&
                Directory.Exists("/data") && File.Exists("/system/build.prop")
            ) {
                os = OSKind.Android;
            } else if (os == OSKind.Posix &&
                Directory.Exists("/Applications") && Directory.Exists("/System") &&
                Directory.Exists("/User") && !Directory.Exists("/Users")
            ) {
                os = OSKind.IOS;
            } else if (os == OSKind.Windows &&
                CheckWine()
            ) {
                // Sorry, Wine devs, but you might want to look at DetourRuntimeNETPlatform.
                os = OSKind.Wine;
            }

            MMDbgLog.Log($"Platform info: {os} {arch}");
            return (os, arch);
        }

        private const string LibC = "libc";
        private const string LibSystem = "libSystem";
        private const string Kernel32 = "Kernel32";

        #region OS-specific arch detection

        // If this dllimport decl isn't enough to get the runtime to load the right thing, I give up
        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        private static extern unsafe int LibcUname(byte* buf);

        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        private static extern unsafe int OSXUname(byte* buf);

        private static unsafe int PosixUname(OSKind os, byte* buf) {
            static int Libc(byte* buf) => LibcUname(buf);
            static int Osx(byte* buf) => Osx(buf);
            return os == OSKind.OSX ? Osx(buf) : Libc(buf);
        }

        private static unsafe string GetCString(ReadOnlySpan<byte> buffer, out int nullByte) {
            fixed (byte* buf = buffer) {
                return Marshal.PtrToStringAnsi((IntPtr)buf, nullByte = buffer.IndexOf((byte)0));
            }
        }

        private static void DetectInfoPosix(ref OSKind os, ref Arch arch) {
            try {
                // we want to call libc's uname() function
                // the fields we're interested in are sysname and machine, which are field 0 and 4 respectively.

                // Unfortunately for us, the size of the utsname struct depends heavily ont he platform. Fortunately for us,
                // the returned data is all null-terminated strings. Hopefully, the unused data in the fields are filled with
                // zeroes or untouched, which will allow us to easily scan for the strings.

                // Because the amount of space required for this syscall is unknown, we'll just allocate 6*513 bytes for it, and scan.

                Span<byte> buffer = new byte[6 * 513];
                unsafe {
                    fixed (byte* bufPtr = buffer) {
                        if (PosixUname(os, bufPtr) < 0) {
                            // uh-oh, uname failed. Log the error if we can  get it and return normally.
                            string msg = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                            MMDbgLog.Log($"uname() syscall failed! {msg}");
                            return;
                        }
                    }
                }

                // buffer now contains a bunch of null-terminated strings
                // the first of these is the kernel name

                var kernelName = GetCString(buffer, out var nullByteOffs).ToUpperInvariant();
                buffer = buffer.Slice(nullByteOffs);

                for (int i = 0; i < 4; i++) { // we want to jump to string 4, but we've already skipped the text of the first
                    if (i != 0) {
                        // skip a string
                        nullByteOffs = buffer.IndexOf((byte)0);
                        buffer = buffer.Slice(nullByteOffs);
                    }
                    // then advance to the next one
                    int j = 0;
                    for (; j < buffer.Length && buffer[j] == 0; j++) { }
                    buffer = buffer.Slice(j);
                }

                // and here we find the machine field
                var machineName = GetCString(buffer, out _).ToUpperInvariant();

                MMDbgLog.Log($"uname() call returned {kernelName} {machineName}");

                // now we want to inspect the fields and select something useful from them
                if (kernelName.Contains("LINUX")) { // A Linux kernel
                    os = OSKind.Linux;
                } else if (kernelName.Contains("DARWIN")) { // the MacOS kernel
                    os = OSKind.OSX;
                } else if (kernelName.Contains("BSD")) { // a BSD kernel
                    // Note: I'm fairly sure that the different BSDs vary quite a lot, so it may be worth checking with more specificity here
                    os = OSKind.BSD;
                }
                // TODO: fill in other known kernel names

                if (machineName.Contains("X86_64")) {
                    arch = Arch.x86_64;
                } else if (machineName.Contains("AMD64")) {
                    arch = Arch.x86_64;
                } else if (machineName.Contains("X86")) {
                    arch = Arch.x86;
                } else if (machineName.Contains("AARCH64")) {
                    arch = Arch.Arm64;
                } else if (machineName.Contains("ARM64")) {
                    arch = Arch.Arm64;
                } else if (machineName.Contains("ARM")) {
                    arch = Arch.Arm;
                }
                // TODO: fill in other values for machine

                MMDbgLog.Log($"uname() detected architecture info: {os} {arch}");
            } catch (Exception e) {
                MMDbgLog.Log($"Error trying to detect info on POSIX-like system");
                MMDbgLog.Log(e.ToString());
                return;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct SystemInfo {
            public ushort wProcessorArchitecture;
            public ushort wReserved1;
            public uint dwPageSize;
            public void* lpMinAppAddr;
            public void* lpMaxAppAddr;
            public nint dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }

        [DllImport(Kernel32, EntryPoint = "GetSystemInfo", SetLastError = false)]
        private static extern void WinGetSystemInfo(out SystemInfo lpSystemInfo);

        private static void DetectInfoWindows(ref OSKind os, ref Arch arch) {
            WinGetSystemInfo(out var sysInfo);

            // we don't update OS here, because Windows

            // https://docs.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-system_info
            arch = sysInfo.wProcessorArchitecture switch {
                9 => Arch.x86_64,
                5 => Arch.Arm,
                12 => Arch.Arm64,
                6 => arch, // Itanium. Fuck Itanium.
                0 => Arch.x86,
                _ => Arch.Unknown
            };
        }
        #endregion

        // Separated method so that this P/Invoke mess doesn't error out on non-Windows.
        private static bool CheckWine() {
            // wine_get_version can be missing because of course it can.
            // General purpose env var.
            string? env = Environment.GetEnvironmentVariable("MONOMOD_WINE");
            if (env == "1")
                return true;
            if (env == "0")
                return false;

            // The "Dalamud" plugin loader for FFXIV uses Harmony, coreclr and wine. What a nice combo!
            // At least they went ahead and provide an environment variable for everyone to check.
            // See https://github.com/goatcorp/FFXIVQuickLauncher/blob/8685db4a0e8ec53235fb08cd88aded7c7061d9fb/src/XIVLauncher/Settings/EnvironmentSettings.cs
            env = Environment.GetEnvironmentVariable("XL_WINEONLINUX")?.ToUpperInvariant();
            if (env == "TRUE")
                return true;
            if (env == "FALSE")
                return false;

            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            if (ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero)
                return true;

            return false;
        }

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [SuppressMessage("Globalization", "CA2101:Specify marshaling for P/Invoke string arguments",
            Justification = "This call needs CharSet = Ansi, and we have BestFitMapping = false and ThrowOnUnmappableChar  = true.")]
        [DllImport(Kernel32,
            CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true,
            BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }
}
