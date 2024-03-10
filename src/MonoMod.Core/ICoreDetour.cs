using System;
using System.Reflection;

namespace MonoMod.Core
{
    /// <summary>
    /// A single method-to-method managed detour.
    /// </summary>
    [CLSCompliant(true)]
    public interface ICoreDetour : ICoreDetourBase
    {
        /// <summary>
        /// The source method.
        /// </summary>
        MethodBase Source { get; }
        /// <summary>
        /// The target method.
        /// </summary>
        MethodBase Target { get; }
    }
}
