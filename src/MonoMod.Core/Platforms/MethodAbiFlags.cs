using System;
using System.Reflection;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// Flags which are used to inform <see cref="PlatformTriple.GetMethodAbi(MethodBase, MethodAbiFlags)"/> of
    /// information about the method which it cannot determine on its own from just the <see cref="MethodBase"/>.
    /// </summary>
    /// <remarks>
    /// These flags are those determinable only by the <see cref="IRuntime"/> implementation for the current runtime.
    /// </remarks>
    [Flags]
    public enum MethodAbiFlags
    {
        /// <summary>
        /// The method requires a generic context parameter.
        /// </summary>
        HasGenericContext = 1 << 0,
        /// <summary>
        /// The method requires an async continuation parameter.
        /// </summary>
        HasAsyncContinuation = 1 << 1,
    }
}
