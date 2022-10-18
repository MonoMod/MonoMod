#if NETCOREAPP3_0_OR_GREATER
using System.Reflection;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Linq;

namespace MonoMod.Utils {
    public static partial class DynDll {

        /// <summary>
        /// Open a given library and get its handle.
        /// </summary>
        /// <param name="name">The library name.</param>
        /// <param name="skipMapping">Whether to skip using the mapping or not.</param>
        /// <param name="flags">Any optional platform-specific flags.</param>
        /// <returns>The library handle.</returns>
        public static IntPtr OpenLibrary(string name, bool skipMapping = false) {
            if (InternalTryOpenLibraryMapping(name, out IntPtr libraryPtr, skipMapping))
                return libraryPtr;
            return NativeLibrary.Load(name, Assembly.GetCallingAssembly(), null);
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
            if (InternalTryOpenLibraryMapping(name, out libraryPtr, skipMapping))
                return true;
            return NativeLibrary.TryLoad(name, Assembly.GetCallingAssembly(), null, out libraryPtr);
        }

        private static bool InternalTryOpenLibraryMapping(string name, out IntPtr libraryPtr, bool skipMapping) {
            if (name != null && !skipMapping && Mappings.TryGetValue(name, out var mappingList)) {
                foreach (DynDllMapping mapping in mappingList) {
                    if (InternalTryOpenLibraryMapping(mapping.LibraryName, out libraryPtr, true))
                        return true;
                }

                libraryPtr = IntPtr.Zero;
                return true;
            }

            libraryPtr = IntPtr.Zero;
            return false;
        }

        /// <summary>
        /// Release a library handle obtained via OpenLibrary. Don't release the result of OpenLibrary(null)!
        /// </summary>
        /// <param name="lib">The library handle.</param>
        public static bool CloseLibrary(IntPtr lib) {
            NativeLibrary.Free(lib);
            return true;
        }

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="libraryPtr">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <returns>The function pointer.</returns>
        public static IntPtr GetFunction(this IntPtr libraryPtr, string name) {
            return NativeLibrary.GetExport(libraryPtr, name);
        }

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="libraryPtr">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <param name="functionPtr">The function pointer, or null if it wasn't found.</param>
        /// <returns>True if the function pointer was obtained, false otherwise.</returns>
        public static bool TryGetFunction(this IntPtr libraryPtr, string name, out IntPtr functionPtr) {
            return NativeLibrary.TryGetExport(libraryPtr, name, out functionPtr);
        }

    }
}
#endif
