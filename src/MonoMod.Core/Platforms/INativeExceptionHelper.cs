using System;

namespace MonoMod.Core.Platforms {
    public interface INativeExceptionHelper {
        bool HasNativeException { get; }

        IntPtr CreateNativeToManagedHelper(IntPtr target, out IDisposable? handle);
        IntPtr CreateManagedToNativeHelper(IntPtr target, out IDisposable? handle);
    }
}
