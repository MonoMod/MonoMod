using System;

namespace MonoMod.Core.Platforms {
    public interface INativeExceptionHelper {
        IntPtr NativeException { get; set; }

        IntPtr CreateNativeToManagedHelper(IntPtr target, out IDisposable? handle);
        IntPtr CreateManagedToNativeHelper(IntPtr target, out IDisposable? handle);
    }
}
