#pragma warning disable IDE1006 // Naming Styles

using System.Reflection;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Linq;

namespace MonoMod.Utils {
    public static class DynDll {

        /// <summary>
        /// Allows you to remap library paths / names and specify loading flags. Useful for cross-platform compatibility. Applies only to DynDll.
        /// </summary>
        public static Dictionary<string, DynDllMapping> Mappings = new Dictionary<string, DynDllMapping>();

        #region kernel32 imports

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hLibModule);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        #endregion

        #region dl imports

        private const int RTLD_LAZY = 0x0001;
        private const int RTLD_NOW = 0x0002;
        private const int RTLD_LOCAL = 0x0000;
        private const int RTLD_GLOBAL = 0x0100;
        [DllImport("dl", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPTStr)] string filename, int flags);
        [DllImport("dl", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool dlclose(IntPtr handle);
        [DllImport("dl", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPTStr)] string symbol);
        [DllImport("dl", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlerror();

        #endregion

        private static T _CheckError<T>(T valueIn)
            => _CheckError(valueIn, out T valueOut, out Exception e) ? valueOut : throw e;
        private static bool _CheckError<T>(T valueIn, out T valueOut, out Exception e) {
            if (PlatformHelper.Is(Platform.Windows)) {
                int err = Marshal.GetLastWin32Error();
                if (err != 0) {
                    valueOut = default;
                    e = new Win32Exception(err);
                    return false;
                }

            } else {
                IntPtr err = dlerror();
                if (err != IntPtr.Zero) {
                    valueOut = default;
                    e = new Win32Exception(Marshal.PtrToStringAnsi(err));
                    return false;
                }
            }

            valueOut = valueIn;
            e = null;
            return true;
        }

        /// <summary>
        /// Open a given library and get its handle.
        /// </summary>
        /// <param name="name">The library name.</param>
        /// <param name="skipMapping">Whether to skip using the mapping or not.</param>
        /// <param name="flags">Any optional platform-specific flags.</param>
        /// <returns>The library handle.</returns>
        public static IntPtr OpenLibrary(string name, bool skipMapping = false, int? flags = null)
            => _CheckError(_OpenLibrary(name, skipMapping, flags), out IntPtr lib, out Exception _e) ? lib : throw _e;

        /// <summary>
        /// Try to open a given library and get its handle.
        /// </summary>
        /// <param name="name">The library name.</param>
        /// <param name="lib">The library handle, or null if it failed loading.</param>
        /// <param name="skipMapping">Whether to skip using the mapping or not.</param>
        /// <param name="flags">Any optional platform-specific flags.</param>
        /// <returns>True if the handle was obtained, false otherwise.</returns>
        public static bool TryOpenLibrary(string name, out IntPtr lib, bool skipMapping = false, int? flags = null)
            => _CheckError(_OpenLibrary(name, skipMapping, flags), out lib, out _);

        public static IntPtr _OpenLibrary(string name, bool skipMapping, int? flags) {
            if (name != null && !skipMapping && Mappings.TryGetValue(name, out DynDllMapping mapping)) {
                name = mapping.ResolveAs ?? name;
                flags = mapping.Flags ?? flags;
            }

            if (PlatformHelper.Is(Platform.Windows)) {
                if (name == null)
                    return GetModuleHandle(name);
                return LoadLibrary(name);

            } else {
                int _flags = flags ?? (RTLD_NOW | RTLD_GLOBAL); // Default should match LoadLibrary.

                IntPtr lib = dlopen(name, _flags);
                if (lib == IntPtr.Zero && File.Exists(name))
                    lib = dlopen(Path.GetFullPath(name), _flags);

                return lib;
            }
        }

        /// <summary>
        /// Release a library handle obtained via OpenLibrary. Don't release the result of OpenLibrary(null)!
        /// </summary>
        /// <param name="lib">The library handle.</param>
        public static bool CloseLibrary(IntPtr lib) {
            if (PlatformHelper.Is(Platform.Windows)) {
                return _CheckError(CloseLibrary(lib));
            } else {
                return _CheckError(dlclose(lib));
            }
        }

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="lib">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <returns>The function pointer.</returns>
        public static IntPtr GetFunction(this IntPtr lib, string name)
            => _CheckError(_GetFunction(lib, name), out IntPtr ptr, out Exception _e) ? ptr : throw _e;

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="lib">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <param name="ptr">The function pointer, or null if it wasn't found.</param>
        /// <returns>True if the function pointer was obtained, false otherwise.</returns>
        public static bool TryGetFunction(this IntPtr lib, string name, out IntPtr ptr)
            => _CheckError(_GetFunction(lib, name), out ptr, out _);

        private static IntPtr _GetFunction(IntPtr lib, string name) {
            if (lib == IntPtr.Zero)
                throw new ArgumentNullException(nameof(lib));

            if (PlatformHelper.Is(Platform.Windows)) {
                return GetProcAddress(lib, name);
            } else {
                return dlsym(lib, name);
            }
        }

        /// <summary>
        /// Extension method wrapping Marshal.GetDelegateForFunctionPointer
        /// </summary>
        public static T AsDelegate<T>(this IntPtr s) where T : class {
#pragma warning disable CS0618 // Type or member is obsolete
            return Marshal.GetDelegateForFunctionPointer(s, typeof(T)) as T;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Fill all static delegate fields with the DynDllImport attribute.
        /// Call this early on in the static constructor.
        /// </summary>
        /// <param name="type">The type containing the DynDllImport delegate fields.</param>
        /// <param name="mappings">Any optional mappings similar to the static mappings.</param>
        public static void ResolveDynDllImports(this Type type, Dictionary<string, DynDllMapping> mappings = null) => _ResolveDynDllImports(type, null, mappings);

        /// <summary>
        /// Fill all instance delegate fields with the DynDllImport attribute.
        /// Call this early on in the constructor.
        /// </summary>
        /// <param name="type">An instance of a type containing the DynDllImport delegate fields.</param>
        /// <param name="mappings">Any optional mappings similar to the static mappings.</param>
        public static void ResolveDynDllImports(object instance, Dictionary<string, DynDllMapping> mappings = null) => _ResolveDynDllImports(instance.GetType(), instance, mappings);

        private static void _ResolveDynDllImports(Type type, object instance, Dictionary<string, DynDllMapping> mappings) {
            BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic;
            if (instance == null)
                fieldFlags |= BindingFlags.Static;
            else
                fieldFlags |= BindingFlags.Instance;

            foreach (FieldInfo field in type.GetFields(fieldFlags)) {
                bool found = true;

                foreach (DynDllImportAttribute attrib in field.GetCustomAttributes(typeof(DynDllImportAttribute), true)) {
                    found = false;

                    bool skipMapping = false;
                    string name = attrib.DLL;
                    int? flags = null;
                    if (mappings != null && (skipMapping = mappings.TryGetValue(name, out DynDllMapping mapping))) {
                        name = mapping.ResolveAs ?? name;
                        flags = mapping.Flags ?? flags;
                    }

                    if (!TryOpenLibrary(name, out IntPtr asm, skipMapping, flags))
                        continue;

                    foreach (string ep in attrib.EntryPoints.Concat(new string[] { field.Name, field.FieldType.Name })) {
                        if (!asm.TryGetFunction(ep, out IntPtr func))
                            continue;
#pragma warning disable CS0618 // Type or member is obsolete
                        field.SetValue(instance, Marshal.GetDelegateForFunctionPointer(func, field.FieldType));
#pragma warning restore CS0618 // Type or member is obsolete
                        found = true;
                        break;
                    }

                    if (found)
                        break;
                }

                if (!found)
                    throw new
#if NETSTANDARD1_X
                        Exception
#else
                        EntryPointNotFoundException
#endif
                        ($"No matching entry point found for {field.Name} in {field.DeclaringType.FullName}");
            }
        }

    }

    /// <summary>
    /// Similar to DllImport, but requires you to run typeof(DeclaringType).ResolveDynDllImports();
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class DynDllImportAttribute : Attribute {
        public string DLL;
        public string[] EntryPoints;
        [Obsolete("Pass the entry points as parameters instead.")]
        public string EntryPoint {
            set => EntryPoints = new string[] { value };
        }
        public DynDllImportAttribute(string dll, params string[] entryPoints) {
            DLL = dll;
            EntryPoints = entryPoints;
        }
    }

    public sealed class DynDllMapping {
        /// <summary>
        /// The name as which the library will be resolved as. Useful to remap libraries or to provide full paths.
        /// </summary>
        public string ResolveAs;

        /// <summary>
        /// Platform-dependant loading flags.
        /// </summary>
        public int? Flags;
    }
}