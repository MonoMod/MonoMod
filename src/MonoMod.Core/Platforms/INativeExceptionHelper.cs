using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A native exception helper to enable interop with native code that throws exceptions.
    /// </summary>
    /// <remarks>
    /// <para>This helper must only be used when native->managed->native transitions are present. If the only transition
    /// is managed->native, this helper cannot be used.</para>
    /// <para>This helper can only propagate exceptions, not catch them permanently.</para>
    /// <para>Use <see cref="CreateNativeToManagedHelper(IntPtr, out IDisposable?)"/> to generate a thunk which should be
    /// used at the native->managed transition. This will rethrow the exception on the current thread, if there is one.
    /// Similarly, use <see cref="CreateManagedToNativeHelper(IntPtr, out IDisposable?)"/> to generate a thunk to use at
    /// the managed->native transition. This thunk will catch exceptions which are thrown in the native code, and store
    /// them in a thread local associated with the exception helper.</para>
    /// </remarks>
    public interface INativeExceptionHelper
    {
        /// <summary>
        /// Gets a delegate which can be used to get a pointer to the current thread's native exception slot.
        /// </summary>
        /// <remarks>
        /// Native exceptions which are caught this way <i>must</i> be preserved and restored just before returning to the 
        /// native->managed helper. Because calls into the native->managed helper clear the thread local storage, this value
        /// must be read and preserved immediately after any calls to the managed->native helper, to protect against other
        /// uses of the exception helper. It must then be restored before returning to the native->managed helper.
        /// </remarks>
        GetExceptionSlot GetExceptionSlot { get; }

        /// <summary>
        /// Creates a native to managed thunk for this exception helper.
        /// </summary>
        /// <param name="target">The function pointer for the generated thunk to call. This is usually the result of <see cref="Marshal.GetFunctionPointerForDelegate(Delegate)"/>.</param>
        /// <param name="handle">A handle to any memory allocations made for the thunk. This must be kept alive as long as the returned pointer is in use.</param>
        /// <returns>A pointer to the thunk to pass to native code to call instead of <paramref name="target"/>.</returns>
        IntPtr CreateNativeToManagedHelper(IntPtr target, out IDisposable? handle);
        /// <summary>
        /// Creates a managed to native thunk for this exception helper.
        /// </summary>
        /// <param name="target">The function pointer for the native code to call.</param>
        /// <param name="handle">A handle to any memory allocations made for the thunk. This must be kept alive as long as the returned pointer is in use.</param>
        /// <returns>A pointer to the thunk for managed code to call instead of <paramref name="target"/>.</returns>
        IntPtr CreateManagedToNativeHelper(IntPtr target, out IDisposable? handle);
    }

    /// <summary>
    /// A delegate which gets a pointer to the current thread's native exception slot.
    /// </summary>
    /// <returns>A pointer to the current thread's native exception slot.</returns>
    public unsafe delegate IntPtr* GetExceptionSlot();
}
