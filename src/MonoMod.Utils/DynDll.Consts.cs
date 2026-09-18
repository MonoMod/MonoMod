// Based on Rust's https://doc.rust-lang.org/std/env/consts/index.html

using System;

namespace MonoMod.Utils
{
    public static partial class DynDll
    {
        /// <summary>
        /// Specifies the file name prefix, if any, used for shared libraries on this platform.
        /// This is either <c>"lib"</c> or an empty string.
        /// </summary>
        public static string DllPrefix
        {
            get
            {
                if (field == null)
                {
                    var os = PlatformDetection.OS;
                    if (os.Is(OSKind.Windows)) field = "";
                    else if (os.Is(OSKind.Posix)) field = "lib";
                    else throw new PlatformNotSupportedException($"OS kind {os} not supported");
                }

                return field;
            }
        }

        /// <summary>
        /// Specifies the file extension, if any, used for shared libraries on this platform that goes after the dot.
        /// An example value may be: <c>"dll"</c>, <c>"dylib"</c>, or <c>"so"</c>.
        /// </summary>
        public static string DllExtension
        {
            get
            {
                if (field == null)
                {
                    var os = PlatformDetection.OS;
                    if (os.Is(OSKind.Windows)) field = "dll";
                    else if (os.Is(OSKind.OSX)) field = "dylib";
                    else if (os.Is(OSKind.Posix)) field = "so";
                    else throw new PlatformNotSupportedException($"OS kind {os} not supported");
                }

                return field;
            }
        }

        /// <summary>
        /// Specifies the file name suffix, if any, used for shared libraries on this platform.
        /// An example value may be: <c>".dll"</c>, <c>".dylib"</c>, or <c>".so"</c>.
        ///
        /// The possible values are identical to those of <see cref="DllExtension"/>, but with the leading period included.
        /// </summary>
        public static string DllSuffix => field ??= "." + DllExtension;

        /// <summary>
        /// Constructs a conventional shared library file name by wrapping the specified <paramref name="name"/> in <see cref="DllPrefix"/> and <see cref="DllSuffix"/>.
        /// </summary>
        /// <param name="name">The name of the shared library.</param>
        public static string MakeDllName(string name) => $"{DllPrefix}{name}{DllSuffix}";

        /// <summary>
        /// Specifies the file extension, if any, used for executable binaries on this platform.
        /// An example value may be: <c>"exe"</c>, or an empty string.
        /// </summary>
        public static string ExeExtension
        {
            get
            {
                if (field == null)
                {
                    var os = PlatformDetection.OS;
                    if (os.Is(OSKind.Windows)) field = "exe";
                    else if (os.Is(OSKind.Posix)) field = "";
                    else throw new PlatformNotSupportedException($"OS kind {os} not supported");
                }

                return field;
            }
        }

        /// <summary>
        /// Specifies the file name suffix, if any, used for executable binaries on this platform.
        /// An example value may be: <c>".exe"</c>, or an empty string.
        ///
        /// The possible values are identical to those of <see cref="ExeExtension"/>, but with the leading period included.
        /// </summary>
        public static string ExeSuffix => field ??= string.IsNullOrEmpty(ExeExtension) ? "" : "." + ExeExtension;
    }
}
