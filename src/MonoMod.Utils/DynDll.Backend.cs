using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonoMod.Utils
{
    public partial class DynDll
    {

#if NETCOREAPP3_0_OR_GREATER
        private static readonly NativeLibraryBackend Backend = new();
#else
        private static readonly BackendImpl Backend = CreateCrossplatBackend();
#endif
        private static BackendImpl CreateCrossplatBackend()
        {
            var os = PlatformDetection.OS;
            if (os.Is(OSKind.Windows))
            {
                return new WindowsBackend();
            }
            else if (os.Is(OSKind.Linux) || os.Is(OSKind.OSX))
            {
                return new LinuxOSXBackend(os.Is(OSKind.Linux));
            }
            else
            {
                MMDbgLog.Warning($"Unknown OS {os} when setting up DynDll; assuming posix-like");
                Helpers.DAssert(os.Is(OSKind.Posix));
                return new UnknownPosixBackend();
            }
        }

        private abstract class BackendImpl
        {
            protected BackendImpl() { }

            protected abstract bool TryOpenLibraryCore(string? name, Assembly assembly, out IntPtr handle);
            public abstract bool TryCloseLibrary(IntPtr handle);
            public abstract bool TryGetExport(IntPtr handle, string name, out IntPtr ptr);
            protected abstract void CheckAndThrowError();

            public virtual bool TryOpenLibrary(string? name, Assembly assembly, out IntPtr handle)
            {
                // NOTE: By default, we perform a manual lookup sequence. The NativeLibrary backend will override this.
                if (name is not null)
                {
                    foreach (var path in GetLibrarySearchOrder(name))
                    {
                        if (TryOpenLibraryCore(path, assembly, out handle))
                            return true;
                    }
                    handle = IntPtr.Zero;
                    return false;
                }
                else
                {
                    return TryOpenLibraryCore(null, assembly, out handle);
                }
            }

            protected virtual IEnumerable<string> GetLibrarySearchOrder(string name)
            {
                yield return name;
            }

            public virtual IntPtr OpenLibrary(string? name, Assembly assembly)
            {
                if (!TryOpenLibrary(name, assembly, out var result))
                    CheckAndThrowError();
                return result;
            }

            public virtual void CloseLibrary(IntPtr handle)
            {
                if (!TryCloseLibrary(handle))
                    CheckAndThrowError();
            }

            public virtual IntPtr GetExport(IntPtr handle, string name)
            {
                if (!TryGetExport(handle, name, out var result))
                    CheckAndThrowError();
                return result;
            }
        }

#if NETCOREAPP3_0_OR_GREATER
        private sealed class NativeLibraryBackend : BackendImpl
        {
#if !NET7_0_OR_GREATER
            private readonly BackendImpl xplatBackend = CreateCrossplatBackend();
#endif

            protected override void CheckAndThrowError()
            {
                throw new NotSupportedException();
            }

#if NET7_0_OR_GREATER
            [SuppressMessage("Performance", "CA1822:Mark members as static",
                Justification = "This isn't static-able in non-NET7 targets")]
#endif
            private IntPtr GetMainProgramHandle(Assembly assembly)
            {
#if NET7_0_OR_GREATER
                return NativeLibrary.GetMainProgramHandle();
#else
                return xplatBackend.OpenLibrary(null, assembly);
#endif
            }

            protected override bool TryOpenLibraryCore(string? name, Assembly assembly, out IntPtr handle)
            {
                if (name is null)
                {
                    handle = GetMainProgramHandle(assembly);
                    return true;
                }
                else
                {
                    return NativeLibrary.TryLoad(name, assembly, null, out handle);
                }
            }

            public override IntPtr OpenLibrary(string? name, Assembly assembly)
            {
                if (name is null)
                {
                    return GetMainProgramHandle(assembly);
                }
                else
                {
                    return NativeLibrary.Load(name, assembly, null);
                }
            }

            // NativeLibrary does search paths for us
            public override bool TryOpenLibrary(string? name, Assembly assembly, out IntPtr handle)
                => TryOpenLibraryCore(name, assembly, out handle);

            public override bool TryCloseLibrary(IntPtr handle)
            {
                NativeLibrary.Free(handle);
                return true;
            }

            public override bool TryGetExport(IntPtr handle, string name, out IntPtr ptr)
            {
                return NativeLibrary.TryGetExport(handle, name, out ptr);
            }

            public override void CloseLibrary(IntPtr handle)
                => TryCloseLibrary(handle);

            public override IntPtr GetExport(IntPtr handle, string name)
            {
                return NativeLibrary.GetExport(handle, name);
            }
        }
#endif

        private sealed class WindowsBackend : BackendImpl
        {
            protected override void CheckAndThrowError()
            {
                var lastError = Interop.Windows.GetLastError();
                if (lastError != 0)
                    throw new Win32Exception((int)lastError);
            }

            protected override unsafe bool TryOpenLibraryCore(string? name, Assembly assembly, out IntPtr handle)
            {
                IntPtr result;
                if (name is null)
                {
                    handle = result = Interop.Windows.GetModuleHandleW(null);
                }
                else
                {
                    fixed (char* pName = name)
                    {
                        handle = result = Interop.Windows.LoadLibraryW((ushort*)pName);
                    }
                }
                return result != IntPtr.Zero;
            }

            public override unsafe bool TryCloseLibrary(IntPtr handle)
            {
                return Interop.Windows.FreeLibrary(new((void*)handle));
            }

            public override unsafe bool TryGetExport(IntPtr handle, string name, out IntPtr ptr)
            {
                var arr = Interop.Unix.MarshalToUtf8(name);
                IntPtr result;
                fixed (byte* pName = arr)
                {
                    ptr = result = Interop.Windows.GetProcAddress(new((void*)handle), (sbyte*)pName);
                }
                Interop.Unix.FreeMarshalledArray(arr);
                return result != IntPtr.Zero;
            }

            protected override IEnumerable<string> GetLibrarySearchOrder(string name)
            {
                yield return name;
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    yield return name + ".dll";
                }
            }
        }

        private abstract class LibdlBackend : BackendImpl
        {
            protected LibdlBackend()
            {
                _ = Interop.Unix.DlError(); // eagerly resolvee this export, so it doesn't muck up later uses
            }

            // Note: this class will be a singleton; this is acceptable
            [ThreadStatic]
            private static IntPtr lastDlErrorReturn;

            [DoesNotReturn]
            private static void ThrowError(IntPtr dlerr)
            {
                throw new Win32Exception(Marshal.PtrToStringAnsi(dlerr));
            }

            protected override void CheckAndThrowError()
            {
                IntPtr errorCode;

                var lastError = lastDlErrorReturn;
                if (lastError == IntPtr.Zero)
                {
                    errorCode = Interop.Unix.DlError();
                }
                else
                {
                    errorCode = lastError;
                    lastDlErrorReturn = IntPtr.Zero;
                }

                if (errorCode != IntPtr.Zero)
                {
                    ThrowError(errorCode);
                }
            }

            protected override bool TryOpenLibraryCore(string? name, Assembly assembly, out IntPtr handle)
            {
                IntPtr result;
                var flags = Interop.Unix.DlopenFlags.RTLD_NOW | Interop.Unix.DlopenFlags.RTLD_GLOBAL;
                handle = result = Interop.Unix.DlOpen(name, flags);
                return result != IntPtr.Zero;
            }

            public override bool TryCloseLibrary(IntPtr handle)
            {
                return Interop.Unix.DlClose(handle);
            }

            public override bool TryGetExport(IntPtr handle, string name, out IntPtr ptr)
            {
                _ = Interop.Unix.DlError(); // clear errors
                ptr = Interop.Unix.DlSym(handle, name); // resolve name
                var dlErr = lastDlErrorReturn = Interop.Unix.DlError(); // check dlerror again
                // if dlErr is null, the result is OK even is result is null (see man page)
                return dlErr == IntPtr.Zero;
            }

            public override IntPtr GetExport(IntPtr handle, string name)
            {
                _ = Interop.Unix.DlError(); // clear errors
                var result = Interop.Unix.DlSym(handle, name); // resolve name
                var dlerr = Interop.Unix.DlError(); // check dlerror again
                // if dlErr is null, the result is OK even is result is null (see man page)
                if (dlerr != IntPtr.Zero)
                {
                    ThrowError(dlerr);
                }
                return result;
            }
        }

        private sealed class LinuxOSXBackend : LibdlBackend
        {
            private readonly bool isLinux;

            public LinuxOSXBackend(bool isLinux)
            {
                this.isLinux = isLinux;
            }

            protected override IEnumerable<string> GetLibrarySearchOrder(string name)
            {
                var hasSlash = name.Contains('/', StringComparison.Ordinal);
                var suffix = ".dylib"; // we set the Linux suffix in the below linux block
                if (isLinux)
                {
                    if (name.EndsWith(".so", StringComparison.Ordinal) || name.Contains(".so.", StringComparison.Ordinal))
                    {
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

                if (isLinux && name is "c" or "libc")
                {
                    // try .so.6 as well (this will include libc.so.6)
                    foreach (var n in GetLibrarySearchOrder("c.so.6"))
                    {
                        yield return n;
                    }
                    // also try glibc if trying to load libc
                    foreach (var n in GetLibrarySearchOrder("glibc"))
                    {
                        yield return n;
                    }
                    foreach (var n in GetLibrarySearchOrder("glibc.so.6"))
                    {
                        yield return n;
                    }
                }
            }
        }

        private sealed class UnknownPosixBackend : LibdlBackend { }
    }
}
