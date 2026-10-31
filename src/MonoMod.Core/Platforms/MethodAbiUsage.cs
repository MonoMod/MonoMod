
using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// An enum used to specify which method ABI usage to compute the effective ABI for.
    /// </summary>
    /// <remarks>
    /// <para>In particular, this may differ for generic methods, where the method's <see cref="RealMethodBody"/>
    /// may require a generic context pointer, but the <see cref="NormalEntry"/> which e.g. <see cref="RuntimeMethodHandle.GetFunctionPointer"/>
    /// returns points to a stub which populates the generic context parameter.</para>
    /// </remarks>
    public enum MethodAbiUsage
    {
        /// <summary>
        /// Indicates that the ABI will be used for something involving the real body of the method.
        /// </summary>
        RealMethodBody,
        /// <summary>
        /// Indicates that the ABI will be used for the normal entrypoint of the method.
        /// </summary>
        NormalEntry,
    }
}
