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
        /// <para>Some methods have multiple underlying method handles which may be relevant. This method returns only the "normal" one.</para>
        /// </remarks>
        /// <param name="method">The method to get the handle of.</param>
        /// <returns>The <see cref="RuntimeMethodHandle"/> of the normal underlying method.</returns>
        RuntimeMethodHandle GetMethodHandle(MethodBase method);

        /// <summary>
        /// Gets the <see cref="MethodAbiFlags"/> for a given <see cref="MethodBase"/>, optionally specifying a specific
        /// variant via <paramref name="handle"/>.
        /// </summary>
        /// <remarks>
        /// <para>In an ideal world, <paramref name="handle"/> would be sufficient to uniquely identify the ABI we care about.
        /// Unfortunately, we currently have many runtimes where we can't find "hidden" method handles, and so cannot in practice
        /// identify certain things by them. We thus use <paramref name="usage"/> as a workaround so we can do this determination
        /// based on our usage patterns alone.</para>
        /// </remarks>
        /// <param name="usage">The <see cref="MethodAbiUsage"/> indicating which method usage to consider.</param>
        /// <param name="method">The method to check.</param>
        /// <param name="handle">The <see cref="RuntimeMethodHandle"/> of <paramref name="method"/> to check. This
        /// MUST be a valid <see cref="RuntimeMethodHandle"/> corresponding to <paramref name="method"/> if it is specified.</param>
        /// <returns>The <see cref="MethodAbiFlags"/> for the <paramref name="method"/> variant identified by <paramref name="handle"/>.</returns>
        MethodAbiFlags GetMethodAbiFlags(MethodAbiUsage usage, MethodBase method, RuntimeMethodHandle handle = default);

        /// <summary>
        /// Disables inlining for a particular method. After this is called, future invocations of this method will not be inlined at the callsite by the runtime.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.DisableInlining"/>.
        /// Typically, callers should use <see cref="PlatformTriple.TryDisableInlining(MethodBase, RuntimeMethodHandle)"/>, which automatically
        /// checks that feature flag, calling this method if available.</para>
        /// </remarks>
        /// <param name="method">The method to disable inlining for.</param>
        /// <param name="handle">The <see cref="RuntimeMethodHandle"/> of <paramref name="method"/> to disable inlining for. This
        /// MUST be a valid <see cref="RuntimeMethodHandle"/> corresponding to <paramref name="method"/> if it is specified.</param>
        void DisableInlining(MethodBase method, RuntimeMethodHandle handle = default);

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
        /// <param name="handle">The <see cref="RuntimeMethodHandle"/> of <paramref name="method"/> to get the entrypoint of. This
        /// MUST be a valid <see cref="RuntimeMethodHandle"/> corresponding to <paramref name="method"/> if it is specified.</param>
        /// <returns>A pointer to the real entrypoint of the method.</returns>
        IntPtr GetMethodEntryPoint(MethodBase method, RuntimeMethodHandle handle = default);

        /// <summary>
        /// Compiles the provided method so that it has a native method body.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresCustomMethodCompile"/>.
        /// Typically, callers should use <see cref="PlatformTriple.Compile(MethodBase, RuntimeMethodHandle)"/>, which automatically calls this method
        /// when available.</para>
        /// <para>When an implementer sets the feature flag <see cref="RuntimeFeature.RequiresCustomMethodCompile"/>, it takes on the full
        /// responsibility of ensuring that a method is compiled. No additional work is done by <see cref="PlatformTriple"/>.</para>
        /// </remarks>
        /// <param name="method">The method to compile.</param>
        /// <param name="handle">The <see cref="RuntimeMethodHandle"/> of <paramref name="method"/> to compile. This
        /// MUST be a valid <see cref="RuntimeMethodHandle"/> corresponding to <paramref name="method"/> if it is specified.</param>
        void Compile(MethodBase method, RuntimeMethodHandle handle = default);

        /// <summary>
        /// Gets information about how the runtime's async machinery treats a method, if it does so specially.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.HasRuntimeAsync"/>.</para>
        /// <para>If <paramref name="runtimeAsyncInfo"/> is populated, all non-<see langword="default"/> method handles are valid
        /// handles corresponding to <paramref name="method"/>.</para>
        /// </remarks>
        /// <param name="method">The method to get runtime async info for.</param>
        /// <param name="runtimeAsyncInfo">The resulting info, if any was found.</param>
        /// <returns><see langword="true"/> if <paramref name="method"/> is treated specially by the runtime's async machinery,
        /// and <paramref name="runtimeAsyncInfo"/> is populated. <see langword="false"/> otherwise.</returns>
        bool TryGetRuntimeAsyncInfo(MethodBase method, out RuntimeAsyncMethodInfo runtimeAsyncInfo);

        /// <summary>
        /// Gets the real method to detour a runtime async's async variant to.
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.HasRuntimeAsync"/>.</para>
        /// <para><see cref="GetAsyncVariantRealDetourTargetArgs.From"/> must be a method for which
        /// <see cref="TryGetRuntimeAsyncInfo(MethodBase, out RuntimeAsyncMethodInfo)"/> returns <see langword="true"/>.
        /// <see cref="GetAsyncVariantRealDetourTargetArgs.FromAsyncVariant"/> must be the <see cref="RuntimeMethodHandle"/> referring
        /// to <see cref="GetAsyncVariantRealDetourTargetArgs.From"/>'s async variant. The specified <see cref="GetAsyncVariantRealDetourTargetArgs.To"/>
        /// variant must be ABI-compatible with <see cref="GetAsyncVariantRealDetourTargetArgs.From"/> according to <paramref name="argumentMap"/>.</para>
        /// <para>All methods passed to this method, the returned <paramref name="handle"/>, and the indirection slot in
        /// <see cref="GetAsyncVariantRealDetourTargetArgs.FromOrigEntrypointSlot"/> must be kept alive as long as the returned method is in use.</para>
        /// </remarks>
        /// <param name="args">A <see cref="GetAsyncVariantRealDetourTargetArgs"/> structure containing detailed arguments to this call.</param>
        /// <param name="argumentMap">The <see cref="AbiArgumentMap"/> specifying how to pass the parameters of
        /// <see cref="GetAsyncVariantRealDetourTargetArgs.From"/> to <see cref="GetAsyncVariantRealDetourTargetArgs.To"/>.</param>
        /// <param name="handle">On return, holds a handle to resources required to keep the detour method functional.</param>
        /// <returns>A tuple of the <see cref="MethodInfo"/> of the new target method, and the <see cref="RuntimeMethodHandle"/> of
        /// the method variant of the method.</returns>
        (MethodInfo Method, RuntimeMethodHandle Variant) GetAsyncVariantRealDetourTarget(
            GetAsyncVariantRealDetourTargetArgs args,
            AbiArgumentMap argumentMap,
            out IDisposable? handle);
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

    /// <summary>
    /// Arguments for <see cref="IRuntime.GetAsyncVariantRealDetourTarget(GetAsyncVariantRealDetourTargetArgs, AbiArgumentMap, out IDisposable?)"/>.
    /// </summary>
    /// <param name="From">The async method to detour from.</param>
    /// <param name="FromAsyncVariant">The async variant of <paramref name="From"/> being detoured.</param>
    /// <param name="To">The method being detoured to.</param>
    /// <param name="ToVariant">The variant of <paramref name="To"/> being detoured to.</param>
    /// <param name="FromOrigEntrypointSlot">A pointer to a slot which, once the detour is configured, will hold a pointer to an entrypoint which
    /// invokes <paramref name="FromAsyncVariant"/> (and not a clone).</param>
    public readonly record struct GetAsyncVariantRealDetourTargetArgs(
        MethodBase From, RuntimeMethodHandle FromAsyncVariant,
        MethodBase To, RuntimeMethodHandle ToVariant,
        IntPtr FromOrigEntrypointSlot);
}
