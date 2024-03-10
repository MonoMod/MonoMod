using System;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A set of features which may be provided by an <see cref="IRuntime"/> implementation.
    /// </summary>
    [Flags]
    public enum RuntimeFeature
    {
        /// <summary>
        /// No features are provided.
        /// </summary>
        None,

        /// <summary>
        /// This runtime uses (or may use) a precise GC, so object references and byrefs must not be stored e.g. as <see cref="IntPtr"/>s.
        /// </summary>
        PreciseGC = 0x01,
        /// <summary>
        /// This runtime hooks the JIT, and invokes <see cref="IRuntime.OnMethodCompiled"/>.
        /// </summary>
        CompileMethodHook = 0x02,

        /// <summary>
        /// This runtime supports detouring natively.
        /// </summary>
        /// <remarks>
        /// Currently, there are no runtimes which support this. However, it is possible that there will be a runtime in the future which does.
        /// </remarks>
        // No runtime supports this *at all* at the moment, but it's here for future use
        ILDetour = 0x04,

        /// <summary>
        /// This runtime uses generic sharing.
        /// </summary>
        GenericSharing = 0x08,
        /// <summary>
        /// This runtime supports listing the instantiations of a generic method.
        /// </summary>
        ListGenericInstantiations = 0x40,

        /// <summary>
        /// This runtime supports disabling inlining on methods, and implements <see cref="IRuntime.DisableInlining(System.Reflection.MethodBase)"/>.
        /// </summary>
        DisableInlining = 0x10,
        /// <summary>
        /// Thus runtime supports un-inlining previously inlined methods.
        /// </summary>
        /// <remarks>
        /// Currently, there are no runtimes which support this. However, it is possible that there will be a runtime in the future which does.
        /// </remarks>
        Uninlining = 0x20,

        /// <summary>
        /// This runtime requires method pinning for detours to function reliably.
        /// </summary>
        RequiresMethodPinning = 0x80,
        /// <summary>
        /// This runtime requires method identification, and implements <see cref="IRuntime.GetIdentifiable(System.Reflection.MethodBase)"/>.
        /// </summary>
        RequiresMethodIdentification = 0x100,

        /// <summary>
        /// This runtime requires method body thunk walking to find the actual method body.
        /// </summary>
        RequiresBodyThunkWalking = 0x200,

        /// <summary>
        /// This runtime has a known ABI.
        /// </summary>
        HasKnownABI = 0x400,

        /// <summary>
        /// This method requires a custom implementation to reliably compile methods, and implements <see cref="IRuntime.Compile(System.Reflection.MethodBase)"/>.,
        /// </summary>
        RequiresCustomMethodCompile = 0x800,

        // TODO: what other runtime feature flags would be useful to have?
    }
}
