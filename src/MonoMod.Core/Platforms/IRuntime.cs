using MonoMod.Utils;
using System;
using System.Reflection;

namespace MonoMod.Core.Platforms {
    public interface IRuntime {
        RuntimeKind Target { get; }

        RuntimeFeature Features { get; }

        Abi Abi { get; }

        event OnMethodCompiledCallback? OnMethodCompiled;

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresMethodIdentification"/>.
        /// Typically, callers should use <see cref="PlatformTriple.GetIdentifiable(MethodBase)"/>, which automatically
        /// checks the feature flag, as well as doing additional processing which is runtime-agnostic.</para>
        /// </remarks>
        /// <param name="method"></param>
        /// <returns></returns>
        MethodBase GetIdentifiable(MethodBase method);

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <para>This method must always be implemented. For simple cases, its implementation can be just a call to
        /// <see cref="MethodBase.MethodHandle"/>, however most runtimes have edge cases which that is not well-behaved for.</para>
        /// </remarks>
        /// <param name="method"></param>
        /// <returns></returns>
        RuntimeMethodHandle GetMethodHandle(MethodBase method);

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.DisableInlining"/>.
        /// Typically, callers should use <see cref="PlatformTriple.TryDisableInlining(MethodBase)"/>, which automatically
        /// checks that feature flag, calling this method if available.</para>
        /// </remarks>
        /// <param name="method"></param>
        void DisableInlining(MethodBase method);

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresMethodPinning"/>.
        /// Typically, callers should use <see cref="PlatformTriple.PinMethodIfNeeded(MethodBase)"/>, which calls this method only
        /// when that flag is set.</para>
        /// </remarks>
        /// <param name="method"></param>
        /// <returns></returns>
        IDisposable? PinMethodIfNeeded(MethodBase method);

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <para>This method must always be implemented. Its result may be treated differently by <see cref="PlatformTriple"/> depending
        /// on whether the implementation sets the flag <see cref="RuntimeFeature.RequiresBodyThunkWalking"/>. If that flag is set, the
        /// return value is presumed to be the start of a thunk chain which eventually leads to the real method body. If it is not set,
        /// the return value of this method is used directly.</para>
        /// </remarks>
        /// <param name="method"></param>
        /// <returns></returns>
        IntPtr GetMethodEntryPoint(MethodBase method);

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// <para>This must only be called if <see cref="Features"/> includes <see cref="RuntimeFeature.RequiresCustomMethodCompile"/>.
        /// Typically, callers should use <see cref="PlatformTriple.Compile(MethodBase)"/>, which automatically calls this method
        /// when available.</para>
        /// <para>When an implementer sets the feature flag <see cref="RuntimeFeature.RequiresCustomMethodCompile"/>, it takes on the full
        /// responsibility of ensuring that a method is compiled. No additional work is done by <see cref="PlatformTriple"/>.</para>
        /// </remarks>
        /// <param name="method"></param>
        void Compile(MethodBase method);
    }

    public delegate void OnMethodCompiledCallback(RuntimeMethodHandle methodHandle, MethodBase? method, IntPtr codeStart, IntPtr codeRw, ulong codeSize);
}
