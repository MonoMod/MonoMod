#if !NETCOREAPP3_0_OR_GREATER
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.IO;
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
        /// <param name="flags">Any optional platform-specific flags.</param>
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
        /// <param name="flags">Any optional platform-specific flags.</param>
        /// <returns>True if the handle was obtained, false otherwise.</returns>
        public static bool TryOpenLibrary(string name, out IntPtr libraryPtr, bool skipMapping = false) {
            return InternalTryOpenLibrary(name, out libraryPtr, skipMapping) || CheckError(out _);
        }

        private static bool InternalTryOpenLibrary(string name, out IntPtr libraryPtr, bool skipMapping) {
            if (name != null && !skipMapping && Mappings.TryGetValue(name, out List<DynDllMapping> mappingList)) {
                foreach (var mapping in mappingList) {
                    if (InternalTryOpenLibrary(mapping.LibraryName, out libraryPtr, true))
                        return true;
                }

                libraryPtr = IntPtr.Zero;
                return true;
            }

            if (PlatformDetection.OS.Is(OSKind.Windows)) {
                libraryPtr = name == null
                    ? Windows.Win32.Interop.GetModuleHandle(name)
                    : Windows.Win32.Interop.LoadLibrary(name);
            } else {
                var flags = Interop.Unix.DlopenFlags.RTLD_NOW | Interop.Unix.DlopenFlags.RTLD_GLOBAL;
                libraryPtr = Interop.Unix.DlOpen(name, flags);

                if (libraryPtr == IntPtr.Zero && File.Exists(name))
                    libraryPtr = Interop.Unix.DlOpen(Path.GetFullPath(name), flags);
            }

            return libraryPtr != IntPtr.Zero;
        }

        /// <summary>
        /// Release a library handle obtained via OpenLibrary. Don't release the result of OpenLibrary(null)!
        /// </summary>
        /// <param name="lib">The library handle.</param>
        public static bool CloseLibrary(IntPtr lib) {
            if (PlatformDetection.OS.Is(OSKind.Windows))
                Windows.Win32.Interop.FreeLibrary((Windows.Win32.Foundation.HINSTANCE) lib);
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

        private static bool InternalTryGetFunction(IntPtr libraryPtr, string name, out IntPtr functionPtr) {
            if (libraryPtr == IntPtr.Zero)
                throw new ArgumentNullException(nameof(libraryPtr));

            functionPtr = PlatformDetection.OS.Is(OSKind.Windows)
                ? Windows.Win32.Interop.GetProcAddress((Windows.Win32.Foundation.HINSTANCE) libraryPtr, name)
                : Interop.Unix.DlSym(libraryPtr, name);

            return functionPtr != IntPtr.Zero;
        }

    }
}
#endif