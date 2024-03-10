using System;
using System.Reflection;

namespace MonoMod.Utils
{
    public static partial class DynDll
    {
        // TODO: remove calls to Assembly.GetCallingAssembly when its not necessary (perhaps by moving them into the backend?)
        // (if we move them into the backend, how can it know how far up to look?)

        /// <summary>
        /// Open a given library and get its handle.
        /// </summary>
        /// <remarks>
        /// Passing <see langword="null"/> to <paramref name="name"/> will get the entrypoint module's handle.
        /// </remarks>
        /// <param name="name">The library name.</param>
        /// <returns>The library handle.</returns>
        public static IntPtr OpenLibrary(string? name)
        {
            return Backend.OpenLibrary(name, Assembly.GetCallingAssembly());
        }

        /// <summary>
        /// Try to open a given library and get its handle.
        /// </summary>
        /// <remarks>
        /// Passing <see langword="null"/> to <paramref name="name"/> will get the entrypoint module's handle.
        /// </remarks>
        /// <param name="name">The library name.</param>
		/// <param name="libraryPtr">The library handle.</param>
        /// <returns><see langword="true"/> if the library was opened successfully; <see langword="false"/> if an error ocurred.</returns>
        public static bool TryOpenLibrary(string? name, out IntPtr libraryPtr)
        {
            return Backend.TryOpenLibrary(name, Assembly.GetCallingAssembly(), out libraryPtr);
        }

        /// <summary>
        /// Release a library handle obtained from <see cref="OpenLibrary(string?)"/> or <see cref="TryOpenLibrary(string?, out IntPtr)"/>.
        /// </summary>
        /// <param name="lib">The library handle.</param>
        public static void CloseLibrary(IntPtr lib)
        {
            Backend.CloseLibrary(lib);
        }

        /// <summary>
        /// Try to close a library handle obtained from <see cref="OpenLibrary(string?)"/> or <see cref="TryOpenLibrary(string?, out IntPtr)"/>.
        /// </summary>
        /// <param name="lib">The library handle.</param>
        /// <returns><see langword="true"/> if the library was closed successfully; <see langword="false"/> if an error ocurred.</returns>
        public static bool TryCloseLibrary(IntPtr lib)
        {
            return Backend.TryCloseLibrary(lib);
        }

        /// <summary>
        /// Get a pointer to an export from the given library.
        /// </summary>
        /// <param name="libraryPtr">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <returns>The function pointer.</returns>
        public static IntPtr GetExport(this IntPtr libraryPtr, string name)
        {
            return Backend.GetExport(libraryPtr, name);
        }

        /// <summary>
        /// Get a pointer to an export from the given library.
        /// </summary>
        /// <param name="libraryPtr">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <param name="functionPtr">The export pointer, or null if it wasn't found.</param>
        /// <returns><see langword="true"/> if the export was obtained successfully; <see langword="false"/> if an error ocurred.</returns>
        public static bool TryGetExport(this IntPtr libraryPtr, string name, out IntPtr functionPtr)
        {
            return Backend.TryGetExport(libraryPtr, name, out functionPtr);
        }

    }
}
