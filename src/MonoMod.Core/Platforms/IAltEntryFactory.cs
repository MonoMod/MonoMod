using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A factory for construction native alternate entrypoints for functions.
    /// </summary>
    public interface IAltEntryFactory
    {
        /// <summary>
        /// Creates an alternate entrypoint for the function at <paramref name="entrypointPc"/>, ensuring that changes to the first <paramref name="minLength"/>
        /// bytes of that function will not interfere with the alternate entrypoint.
        /// </summary>
        /// <remarks>
        /// <para>This method only guarantees that the alternate entrypoint will be valid as long as no more than <paramref name="minLength"/> bytes of
        /// the original entrypoint are changed. While in practice more bytes may be safe, it is is not safe in general to write more bytes. There is currently
        /// not an API which is capable of expanding this space, which means that detours which use this to create an alternate entrypoint cannot be safely
        /// retargeted in general.</para>
        /// </remarks>
        /// <param name="entrypointPc">The PC main entrypoint of the function to create an alternate entrypoint for.</param>
        /// <param name="entrypointCode">The address from which to actually read code. The code will be relocated as if it started
        /// at <paramref name="entrypointPc"/> instead.</param>
        /// <param name="minLength">The minimum number of bytes of code that should be safe to replace.</param>
        /// <param name="handle">A handle to any memory allocations made for this alternate entrypoint.</param>
        /// <returns>A pointer to the created alternate entry point.</returns>
        IntPtr CreateAlternateEntrypoint(IntPtr entrypointPc, IntPtr entrypointCode, int minLength, out IDisposable? handle);
    }
}
