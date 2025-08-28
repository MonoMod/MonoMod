using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A set of features which may be provided by an <see cref="ISystem"/> implementation.
    /// </summary>
    [Flags]
    public enum SystemFeature
    {
        /// <summary>
        /// No features are provided.
        /// </summary>
        None,

        /// <summary>
        /// This system allows for pages which are protected Read/Write/Execute.
        /// </summary>
        RWXPages = 0x01,

        /// <summary>
        /// This system allows for pages which are protected Read/Execute.
        /// </summary>
        RXPages = 0x02,

        /// <summary>
        /// This system may make use of native jit hooks.
        /// </summary>
        MayUseNativeJitHooks = 0x10,
    }
}
