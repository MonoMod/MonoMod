using System.Reflection;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.IO;

namespace MonoMod.Utils {
    public static class DynDll {

        /// <summary>
        /// Allows you to remap library paths / names. Useful for cross-platform compatibility. Applies only to DynDll.
        /// </summary>
        public static Dictionary<string, string> DllMap = new Dictionary<string, string>();

        /// <summary>
        /// Allows you to provide custom flags when loading libraries. Platform-dependant.
        /// </summary>
        public static Dictionary<string, int> DllFlags = new Dictionary<string, int>();

        #region kernel32 imports

        [DllImport("kernel32")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32")]
        private static extern IntPtr LoadLibrary(string lpFileName);
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
        private static extern IntPtr dlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPTStr)] string symbol);
        [DllImport("dl", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlerror();

        #endregion

        /// <summary>
        /// Open a given library and / or get its handle.
        /// </summary>
        /// <param name="name">The library name.</param>
        /// <returns>The library handle, or null if it failed loading.</returns>
        public static IntPtr OpenLibrary(string name) {
            if (name != null && DllMap.TryGetValue(name, out string mapped))
                name = mapped;

            IntPtr lib;

            if (PlatformHelper.Is(Platform.Windows)) {
                lib = GetModuleHandle(name);
                if (lib == IntPtr.Zero) {
                    lib = LoadLibrary(name);
                }
                return lib;
            }

            int mappedFlags;
            if (!DllFlags.TryGetValue(name, out mappedFlags))
                mappedFlags = RTLD_NOW;

            IntPtr e = IntPtr.Zero;
            lib = dlopen(name, mappedFlags);

            if (lib == IntPtr.Zero && File.Exists(name)) {
                lib = dlopen(Path.GetFullPath(name), mappedFlags);
            }

            if ((e = dlerror()) != IntPtr.Zero) {
                Console.WriteLine($"DynDll can't access {name ?? "entry point"}!");
                Console.WriteLine("dlerror: " + Marshal.PtrToStringAnsi(e));
                return IntPtr.Zero;
            }
            return lib;
        }

        /// <summary>
        /// Get a function pointer for a function in the given library.
        /// </summary>
        /// <param name="lib">The library handle.</param>
        /// <param name="name">The function name.</param>
        /// <returns>The function pointer, or null if it wasn't found.</returns>
        public static IntPtr GetFunction(this IntPtr lib, string name) {
            if (lib == IntPtr.Zero)
                return IntPtr.Zero;

            if (PlatformHelper.Is(Platform.Windows))
                return GetProcAddress(lib, name);

            IntPtr s, e;

            s = dlsym(lib, name);
            if ((e = dlerror()) != IntPtr.Zero) {
                Console.WriteLine("DynDll can't access " + name + "!");
                Console.WriteLine("dlerror: " + Marshal.PtrToStringAnsi(e));
                return IntPtr.Zero;
            }
            return s;
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
        /// Fill all delegate fields with the DynDllImport attribute.
        /// Call this early on in the static constructor.
        /// </summary>
        /// <param name="type">The type containing the DynDllImport delegate fields.</param>
        public static void ResolveDynDllImports(this Type type) {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) {
                bool found = true;
                foreach (DynDllImportAttribute attrib in field.GetCustomAttributes(typeof(DynDllImportAttribute), true)) {
                    found = false;
                    IntPtr asm = OpenLibrary(attrib.DLL);
                    if (asm == IntPtr.Zero)
                        continue;

                    foreach (string ep in attrib.EntryPoints) {
                        IntPtr func = asm.GetFunction(ep);
                        if (func == IntPtr.Zero)
                            continue;
#pragma warning disable CS0618 // Type or member is obsolete
                        field.SetValue(null, Marshal.GetDelegateForFunctionPointer(func, field.FieldType));
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
            set {
                EntryPoints = new string[] { value };
            }
        }
        public DynDllImportAttribute(string dll, params string[] entryPoints) {
            DLL = dll;
            EntryPoints = entryPoints;
        }
    }
}