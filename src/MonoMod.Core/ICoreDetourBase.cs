using System;

namespace MonoMod.Core
{
    /// <summary>
    /// A single detour. This is the base type of both <see cref="ICoreDetour"/> and <see cref="ICoreNativeDetour"/>.
    /// </summary>
    /// <remarks>
    /// <para>When disposed or collected by GC, detours will be automatically undone and any associated memory freed.</para>
    /// </remarks>
    /// <seealso cref="ICoreDetour"/>
    /// <seealso cref="ICoreNativeDetour"/>
    public interface ICoreDetourBase : IDisposable
    {
        /// <summary>
        /// Gets whether or not this detour is currently applied.
        /// </summary>
        bool IsApplied { get; }

        /// <summary>
        /// Applies this detour.
        /// </summary>
        void Apply();

        /// <summary>
        /// Undoes this detour. Once a detour is undone, it is no longer valid, and may not be used further.
        /// </summary>
        void Undo();
    }
}
