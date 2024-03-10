using System;

namespace MonoMod.Core
{
    /// <summary>
    /// Represents a single function-to-function native detour, with an alternate entry point.
    /// </summary>
    public interface ICoreNativeDetour : ICoreDetourBase
    {
        /// <summary>
        /// Gets a pointer to the source function.
        /// </summary>
        IntPtr Source { get; }
        /// <summary>
        /// Gets a pointer to the target function.
        /// </summary>
        IntPtr Target { get; }

        /// <summary>
        /// Gets whether or not an alternate entrypoint for the original function is available in <see cref="OrigEntrypoint"/>.
        /// </summary>
        bool HasOrigEntrypoint { get; }
        /// <summary>
        /// Gets the alternate entrypoint for the original function.
        /// </summary>
        /// <remarks>
        /// It is only valid to access this property if <see cref="HasOrigEntrypoint"/> is <see langword="true"/>
        /// </remarks>
        IntPtr OrigEntrypoint { get; }
    }
}
