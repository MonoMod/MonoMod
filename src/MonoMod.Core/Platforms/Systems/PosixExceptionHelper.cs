using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class PosixExceptionHelper : INativeExceptionHelper
    {
        private readonly IArchitecture arch;
        private readonly IntPtr eh_get_exception_ptr;
        private readonly IntPtr eh_managed_to_native;
        private readonly IntPtr eh_native_to_managed;

        private PosixExceptionHelper(IArchitecture arch, IntPtr getExPtr, IntPtr m2n, IntPtr n2m)
        {
            this.arch = arch;
            eh_get_exception_ptr = getExPtr;
            eh_managed_to_native = m2n;
            eh_native_to_managed = n2m;
        }

        public static PosixExceptionHelper CreateHelper(IArchitecture arch, string filename)
        {
            // we've now got the file on disk, and we know its name
            // lets load it
            var handle = DynDll.OpenLibrary(filename);
            IntPtr eh_get_exception_ptr, eh_managed_to_native, eh_native_to_managed;
            try
            {
                eh_get_exception_ptr = DynDll.GetExport(handle, nameof(eh_get_exception_ptr));
                eh_managed_to_native = DynDll.GetExport(handle, nameof(eh_managed_to_native));
                eh_native_to_managed = DynDll.GetExport(handle, nameof(eh_native_to_managed));

                Helpers.Assert(eh_get_exception_ptr != IntPtr.Zero);
                Helpers.Assert(eh_managed_to_native != IntPtr.Zero);
                Helpers.Assert(eh_native_to_managed != IntPtr.Zero);
            }
            catch
            {
                DynDll.CloseLibrary(handle);
                throw;
            }

            return new PosixExceptionHelper(arch, eh_get_exception_ptr, eh_managed_to_native, eh_native_to_managed);
        }

        public unsafe IntPtr NativeException
        {
            get => *((delegate* unmanaged[Cdecl]<IntPtr*>)eh_get_exception_ptr)();
            set => *((delegate* unmanaged[Cdecl]<IntPtr*>)eh_get_exception_ptr)() = value;
        }

        public unsafe GetExceptionSlot GetExceptionSlot => () => ((delegate* unmanaged[Cdecl]<IntPtr*>)eh_get_exception_ptr)();

        public IntPtr CreateManagedToNativeHelper(IntPtr target, out IDisposable? handle)
        {
            var alloc = arch.CreateSpecialEntryStub(eh_managed_to_native, target);
            handle = alloc;
            return alloc.BaseAddress;
        }

        public IntPtr CreateNativeToManagedHelper(IntPtr target, out IDisposable? handle)
        {
            var alloc = arch.CreateSpecialEntryStub(eh_native_to_managed, target);
            handle = alloc;
            return alloc.BaseAddress;
        }
    }
}
