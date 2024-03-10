using MonoMod.Utils;
using System;
using System.Reflection;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// Represents a host .NET runtime.
    /// </summary>
    public interface IRuntime
    {
        /// <summary>
        /// Gets the <see cref="RuntimeKind"/> that this instance represents.
        /// </summary>
        RuntimeKind Target { get; }

        /// <summary>
        /// Gets the set of <see cref="RuntimeFeature"/>s that this instance supports. Some members may only be available with certain feature flags set.
        /// </summary>
        RuntimeFeature Features { get; }

        /// <summary>
        /// Gets the <see cref="Abi"/> descriptor for this runtime.
        /// </summary>
        Abi Abi { get; }

        /// <summary>
        /// An event which is invoked when a method is compiled.
        /// </summary>
        /// <remarks>
        /// This event will only be invoked when <see cref="Features"/> includes <see cref="RuntimeFeature.CompileMethodHook"/>.
        /// </remarks>
        event OnMethodCompiledCallback? OnMethodCompiled;

        /// <summary>
        /// Gets an "identifiable" <see cref="MethodBase"/>. The returned instance is safe to use with object identity to refer to specific methods.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresMethodIdentification"/>.
        /// Typically, callers should use <see cref="PlatformTriple.GetIdentifiable(MethodBase)"/>, which automatically
        /// checks the feature flag, as well as doing additional processing which is runtime-agnostic.</para>
        /// </remarks>
        /// <param name="method">The method to identify.</param>
        /// <returns>A <see cref="MethodBase"/> with object identity.</returns>
        MethodBase GetIdentifiable(MethodBase method);

        /// <summary>
        /// Portably gets the <see cref="RuntimeMethodHandle"/> for a given <see cref="MethodBase"/>.
        /// </summary>
        /// <remarks>
        /// <para>This method must always be implemented. For simple cases, its implementation can be just a call to
        /// <see cref="MethodBase.MethodHandle"/>, however most runtimes have edge cases which that is not well-behaved for.</para>
        /// </remarks>
        /// <param name="method"></param>
        /// <returns></returns>
        RuntimeMethodHandle GetMethodHandle(MethodBase method);

        /// <summary>
        /// Disables inlining for a particular method. After this is called, future invocations of this method will not be inlined at the callsite by the runtime.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.DisableInlining"/>.
        /// Typically, callers should use <see cref="PlatformTriple.TryDisableInlining(MethodBase)"/>, which automatically
        /// checks that feature flag, calling this method if available.</para>
        /// </remarks>
        /// <param name="method">The method to disable inlining for.</param>
        void DisableInlining(MethodBase method);

        /// <summary>
        /// Pins a method so that it will not be garbage collected, if that is necessary for this runtime.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresMethodPinning"/>.
        /// Typically, callers should use <see cref="PlatformTriple.PinMethodIfNeeded(MethodBase)"/>, which calls this method only
        /// when that flag is set.</para>
        /// </remarks>
        /// <param name="method">The method to pin.</param>
        /// <returns>An <see cref="IDisposable"/> representing the method pin, or <see langword="null"/> if none is needed.</returns>
        IDisposable? PinMethodIfNeeded(MethodBase method);

        /// <summary>
        /// Gets the real entrypoint of the provided method.
        /// </summary>
        /// <remarks>
        /// <para>This method must always be implemented. Its result may be treated differently by <see cref="PlatformTriple"/> depending
        /// on whether the implementation sets the flag <see cref="RuntimeFeature.RequiresBodyThunkWalking"/>. If that flag is set, the
        /// return value is presumed to be the start of a thunk chain which eventually leads to the real method body. If it is not set,
        /// the return value of this method is used directly.</para>
        /// </remarks>
        /// <param name="method">The method to get the entrypoint of.</param>
        /// <returns>A pointer to the real entrypoint of the method.</returns>
        IntPtr GetMethodEntryPoint(MethodBase method);

        /// <summary>
        /// Compiles the provided method so that it has a native method body.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresCustomMethodCompile"/>.
        /// Typically, callers should use <see cref="PlatformTriple.Compile(MethodBase)"/>, which automatically calls this method
        /// when available.</para>
        /// <para>When an implementer sets the feature flag <see cref="RuntimeFeature.RequiresCustomMethodCompile"/>, it takes on the full
        /// responsibility of ensuring that a method is compiled. No additional work is done by <see cref="PlatformTriple"/>.</para>
        /// </remarks>
        /// <param name="method">The method to compile.</param>
        void Compile(MethodBase method);
    }

    /// <summary>
    /// A callback which is called when a method is compiled by the JIT.
    /// </summary>
    /// <remarks>
    /// <para>On some runtimes, <paramref name="codeStart"/> != <paramref name="codeRw"/>. When this is the case, <paramref name="codeRw"/> contains the actual body, which will
    /// be copied to <paramref name="codeStart"/> after the hook returns.</para>
    /// </remarks>
    /// <param name="methodHandle">The <see cref="RuntimeMethodHandle"/> of the method which was compiled.</param>
    /// <param name="method">A <see cref="MethodBase"/> representing the method, if one could be found.</param>
    /// <param name="codeStart">A pointer to the start of the method code. This only represents the final location which the code will exist at.</param>
    /// <param name="codeRw">A pointer to the start of the read/write capable code. This contains the actual code data.</param>
    /// <param name="codeSize">The size of the code.</param>
    public delegate void OnMethodCompiledCallback(RuntimeMethodHandle methodHandle, MethodBase? method, IntPtr codeStart, IntPtr codeRw, ulong codeSize);
}
