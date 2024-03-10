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
        public const string DL1 = "dl";
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

        [DllImport(DL1, EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DL1dlopen(byte* filename, DlopenFlags flags);
        [DllImport(DL1, EntryPoint = "dlclose", CallingConvention = CallingConvention.Cdecl)]
        private static extern int DL1dlclose(IntPtr handle);
        [DllImport(DL1, EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DL1dlsym(IntPtr handle, byte* symbol);
        [DllImport(DL1, EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DL1dlerror();


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

        private static int dlVersion = 1;

        public static IntPtr DlOpen(string? filename, DlopenFlags flags)
        {
            var arr = MarshalToUtf8(filename);
            try
            {
                while (true)
                {
                    try
                    {
                        fixed (byte* pStr = arr)
                        {
                            switch (dlVersion)
                            {
                                case 1:
                                    return DL2dlopen(pStr, flags);

                                case 0:
                                default:
                                    return DL1dlopen(pStr, flags);
                            }
                        }
                    }
                    catch (DllNotFoundException) when (dlVersion > 0)
                    {
                        dlVersion--;
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
            while (true)
            {
                try
                {
                    switch (dlVersion)
                    {
                        case 1:
                            return DL2dlclose(handle) == 0;

                        case 0:
                        default:
                            return DL1dlclose(handle) == 0;
                    }
                }
                catch (DllNotFoundException) when (dlVersion > 0)
                {
                    dlVersion--;
                }
            }
        }

        public static IntPtr DlSym(IntPtr handle, string symbol)
        {
            var arr = MarshalToUtf8(symbol);
            try
            {
                while (true)
                {
                    try
                    {
                        fixed (byte* pStr = arr)
                        {
                            switch (dlVersion)
                            {
                                case 1:
                                    return DL2dlsym(handle, pStr);

                                case 0:
                                default:
                                    return DL1dlsym(handle, pStr);
                            }
                        }
                    }
                    catch (DllNotFoundException) when (dlVersion > 0)
                    {
                        dlVersion--;
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
            while (true)
            {
                try
                {
                    switch (dlVersion)
                    {
                        case 1:
                            return DL2dlerror();

                        case 0:
                        default:
                            return DL1dlerror();
                    }
                }
                catch (DllNotFoundException) when (dlVersion > 0)
                {
                    dlVersion--;
                }
            }
        }

    }
}
