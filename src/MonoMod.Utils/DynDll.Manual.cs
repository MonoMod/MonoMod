#if !NETCOREAPP3_0_OR_GREATER
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Utils {
    public static partial class DynDll {

        static DynDll() {
            // Run a dummy dlerror to resolve it so that it won't interfere with the first call
            if (!PlatformDetection.OS.Is(OSKind.Windows))
                Interop.Unix.DlError();
        }

        private static bool CheckError([NotNullWhen(false)] out Exception? exception) {
            if (PlatformDetection.OS.Is(OSKind.Windows)) {
                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode != 0) {
                    exception = new Win32Exception(errorCode);
                    return false;
                }
            } else {
                var errorCode = Interop.Unix.DlError();
                if (errorCode != IntPtr.Zero) {
                    exception = new Win32Exception(Marshal.PtrToStringAnsi(errorCode));
                    return false;
                }
            }

            exception = null;
            return true;
        }

        /// <summary>
        /// Open a given library and get its handle.
        /// </summary>
        /// <param name="name">The library name.</param>
        /// <param name="skipMapping">Whether to skip using the mapping or not.</param>
        /// <returns>The library handle.</returns>
        public static IntPtr OpenLibrary(string name, bool skipMapping = false) {
            if (!InternalTryOpenLibrary(name, out var libraryPtr, skipMapping))
                throw new DllNotFoundException($"Unable to load library '{name}'");

            if (!CheckError(out var exception))
                throw exception;

            return libraryPtr;
        }

        /// <summary>
        /// Try to open a given library and get its handle.
        /// </summary>
        /// <param name="name">The library name.</param>
		/// <param name="libraryPtr">The library handle, or null if it failed loading.</param>
        /// <param name="skipMapping">Whether to skip using the mapping or not.</param>
        /// <returns>True if the handle was obtained, false otherwise.</returns>
        public static bool TryOpenLibrary(string name, out IntPtr libraryPtr, bool skipMapping = false) {
            return InternalTryOpenLibrary(name, out libraryPtr, skipMapping) || CheckError(out _);
        }

        private static bool InternalTryOpenLibrary(string name, out IntPtr libraryPtr, bool skipMapping) {
            Helpers.ThrowIfArgumentNull(name);
            if (name != null && !skipMapping && Mappings.TryGetValue(name, out List<DynDllMapping> mappingList)) {
                foreach (var mapping in mappingList) {
                    if (InternalTryOpenLibrary(mapping.LibraryName, out libraryPtr, true))
                        return true;
                }

                libraryPtr = IntPtr.Zero;
                return true;
            }

            libraryPtr = IntPtr.Zero;
            if (name is not null) {
                foreach (var n in GetLibrarySearchOrder(name)) {
                    libraryPtr = OpenLibraryCore(n);
                    if (libraryPtr != IntPtr.Zero)
                        break;
                }
            } else {
                libraryPtr = OpenLibraryCore(null);
            }

            return libraryPtr != IntPtr.Zero;
        }

        private static unsafe IntPtr OpenLibraryCore(string? name) {
            if (PlatformDetection.OS.Is(OSKind.Windows)) {
                if (name is null) {
                    return (IntPtr)Interop.Windows.GetModuleHandleW(null).Value;
                } else {
                    fixed (char* pName = name) {
                        return (IntPtr)Interop.Windows.LoadLibraryW((ushort*)pName).Value;
                    }
                }
            } else {
                var flags = Interop.Unix.DlopenFlags.RTLD_NOW | Interop.Unix.DlopenFlags.RTLD_GLOBAL;
                return Interop.Unix.DlOpen(name, flags);

                /*if (libraryPtr == IntPtr.Zero && File.Exists(name))
                    libraryPtr = Interop.Unix.DlOpen(Path.GetFullPath(name), flags);*/
            }
        }

        // This replicates the .NET Core P/Invoke resolution order
        // https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cross-platform
        private static IEnumerable<string> GetLibrarySearchOrder(string name) {
            var os = PlatformDetection.OS;
            if (os.Is(OSKind.Windows)) {
                yield return name;
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                    yield return name + ".dll";
                }
            } else if (os.Is(OSKind.Linux) || os.Is(OSKind.OSX)) {
                var hasSlash = name.Contains("/", StringComparison.Ordinal);
                var suffix = ".dylib"; // we set the Linux suffix in the below linux block
                if (os.Is(OSKind.Linux)) {
                    if (name.EndsWith(".so", StringComparison.Ordinal) || name.Contains(".so.", StringComparison.Ordinal)) {
                        yield return name;
                        if (!hasSlash)
                            yield return "lib" + name;
                        yield return name + ".so";
                        if (!hasSlash)
                            yield return "lib" + name + ".so";
                        yield break;
                    }

                    suffix = ".so";
                }

                yield return name + suffix;
                if (!hasSlash)
                    yield return "lib" + name + suffix;
                yield return name;
                if (!hasSlash)
                    yield return "lib" + name;

                if (os.Is(OSKind.Linux) && name is "c" or "libc") {
                    // try .so.6 as well (this will include libc.so.6)
                    foreach (var n in GetLibrarySearchOrder("c.so.6")) {
                        yield return n;
                    }
                    // also try glibc if trying to load libc
                    foreach (var n in GetLibrarySearchOrder("glibc")) {
                        yield return n;
                    }
                    foreach (var n in GetLibrarySearchOrder("glibc.so.6")) {
                        yield return n;
                    }
                }
            } else {
                MMDbgLog.Warning($"Unknown OS {os} when trying to find resolution order for {name}");
                yield return name;
            }
        }

        /// <summary>
        /// Release a library handle obtained via OpenLibrary. Don't release the result of OpenLibrary(null)!
        /// </summary>
        /// <param name="lib">The library handle.</param>
        public static unsafe bool CloseLibrary(IntPtr lib) {
            if (PlatformDetection.OS.Is(OSKind.Windows))
                Interop.Windows.FreeLibrary(new((void*)lib));
            else
                Interop.Unix.DlClose(lib);

            return CheckError(out _);
        }

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="libraryPtr">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <returns>The function pointer.</returns>
        public static IntPtr GetFunction(this IntPtr libraryPtr, string name) {
            if (!InternalTryGetFunction(libraryPtr, name, out var functionPtr))
                throw new MissingMethodException($"Unable to load function '{name}'");

            if (!CheckError(out var exception))
                throw exception;

            return functionPtr;
        }

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="libraryPtr">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <param name="functionPtr">The function pointer, or null if it wasn't found.</param>
        /// <returns>True if the function pointer was obtained, false otherwise.</returns>
        public static bool TryGetFunction(this IntPtr libraryPtr, string name, out IntPtr functionPtr) {
            return InternalTryGetFunction(libraryPtr, name, out functionPtr) || CheckError(out _);
        }

        private static unsafe bool InternalTryGetFunction(IntPtr libraryPtr, string name, out IntPtr functionPtr) {
            if (libraryPtr == IntPtr.Zero)
                throw new ArgumentNullException(nameof(libraryPtr));

            if (PlatformDetection.OS.Is(OSKind.Windows)) {
                var (arr, span) = Interop.Unix.MarshalToUtf8(name);
                fixed (byte* pName = span.Span) {
                    functionPtr = Interop.Windows.GetProcAddress(new((void*) libraryPtr), (sbyte*)pName);
                }
                Interop.Unix.FreeMarshalledArray(arr);
            } else {
                functionPtr = Interop.Unix.DlSym(libraryPtr, name);
            }

            return functionPtr != IntPtr.Zero;
        }

    }
}
#endif