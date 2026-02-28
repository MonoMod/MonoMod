using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.Utils.Interop
{
    [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes",
        Justification = "The attribute doesn't do anything on platforms where this will be used.")]
    internal unsafe static partial class Unix
    {
        // If this dllimport decl isn't enough to get the runtime to load the right thing, I give up
        public const string LibC = "libc";
        public const string DL2 = "libdl.so.2";

        // We have to do these shenanigans, because we *need* SetLastError; this can set errno.
        // SetLastError on DllImport involves an ILStub, and DisableRuntimeMarshalling prevents that.
        // LibraryImport can't be used downlevel for this, because it relies on Marshal.GetLastSystemError(), which is new in .NET 6.
#if NET7_0_OR_GREATER
        [LibraryImport(LibC, EntryPoint = "uname", SetLastError = true)]
        public static unsafe partial int Uname(byte* buf);
#else
        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl, EntryPoint = "uname", SetLastError = true)]
        public static extern unsafe int Uname(byte* buf);
#endif

        [StructLayout(LayoutKind.Sequential)]
        public struct LinuxAuxvEntry
        {
            public nint Key;
            public nint Value;
        }

        public const int AT_PLATFORM = 0xf;

        public enum DlopenFlags : int
        {
            RTLD_LAZY = 0x0001,
            RTLD_NOW = 0x0002,
            RTLD_LOCAL = 0x0000,
            RTLD_GLOBAL = 0x0100,
        }

        [DllImport(LibC, EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LibCdlopen(byte* filename, DlopenFlags flags);
        [DllImport(LibC, EntryPoint = "dlclose", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LibCdlclose(IntPtr handle);
        [DllImport(LibC, EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LibCdlsym(IntPtr handle, byte* symbol);
        [DllImport(LibC, EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LibCdlerror();


        [DllImport(DL2, EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DL2dlopen(byte* filename, DlopenFlags flags);
        [DllImport(DL2, EntryPoint = "dlclose", CallingConvention = CallingConvention.Cdecl)]
        private static extern int DL2dlclose(IntPtr handle);
        [DllImport(DL2, EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DL2dlsym(IntPtr handle, byte* symbol);
        [DllImport(DL2, EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DL2dlerror();

        internal static byte[]? MarshalToUtf8(string? str)
        {
            if (str is null)
                return null;

            var len = Encoding.UTF8.GetByteCount(str);
            var arr = ArrayPool<byte>.Shared.Rent(len + 1);
            arr.AsSpan().Clear();
            var encoded = Encoding.UTF8.GetBytes(str, 0, str.Length, arr, 0);
            Helpers.DAssert(len == encoded);
            return arr;
        }

        internal static void FreeMarshalledArray(byte[]? arr)
        {
            if (arr is null)
                return;
            ArrayPool<byte>.Shared.Return(arr);
        }

        private enum LibDlType
        {
            LibC,
            LibDl2,
        }

        private static LibDlType? currentLibDlType = DetermineLibDlType();

        private static LibDlType DetermineLibDlType()
        {
            // POSIX doesn't specify where the `dlfcn.h` symbols are located at runtime
            // In most cases (MacOS, musl, glibc 2.34+) they live in libc, and its path is already special-cased by mono/coreclr
            // Before version 2.34, glibc had a separate libdl.so.2 library

            try
            {
                LibCdlerror();
                return LibDlType.LibC;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try
            {
                DL2dlerror();
                return LibDlType.LibDl2;
            }
            catch (DllNotFoundException)
            {
            }

            throw new PlatformNotSupportedException("Could not find the library containing dynamic linker functions");
        }

        public static IntPtr DlOpen(string? filename, DlopenFlags flags)
        {
            var arr = MarshalToUtf8(filename);
            try
            {
                fixed (byte* pStr = arr)
                {
                    switch (currentLibDlType)
                    {
                        case LibDlType.LibC: return LibCdlopen(pStr, flags);
                        case LibDlType.LibDl2: return DL2dlopen(pStr, flags);
                        default: throw new InvalidOperationException();
                    }
                }
            }
            finally
            {
                FreeMarshalledArray(arr);
            }
        }

        public static bool DlClose(IntPtr handle)
        {
            switch (currentLibDlType)
            {
                case LibDlType.LibC: return LibCdlclose(handle) == 0;
                case LibDlType.LibDl2: return DL2dlclose(handle) == 0;
                default: throw new InvalidOperationException();
            }
        }

        public static IntPtr DlSym(IntPtr handle, string symbol)
        {
            var arr = MarshalToUtf8(symbol);
            try
            {
                fixed (byte* pStr = arr)
                {
                    switch (currentLibDlType)
                    {
                        case LibDlType.LibC: return LibCdlsym(handle, pStr);
                        case LibDlType.LibDl2: return DL2dlsym(handle, pStr);
                        default: throw new InvalidOperationException();
                    }
                }
            }
            finally
            {
                FreeMarshalledArray(arr);
            }
        }

        public static IntPtr DlError()
        {
            switch (currentLibDlType)
            {
                case LibDlType.LibC: return LibCdlerror();
                case LibDlType.LibDl2: return DL2dlerror();
                default: throw new InvalidOperationException();
            }
        }
    }
}
