using MonoMod.Core;
using MonoMod.Utils;
using System;
using System.Linq;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A single hook from a native function to a target delegate, optionally allowing the target to call the original function.
    /// </summary>
    /// <remarks>
    /// <see cref="NativeHook"/>s, like other kinds of detours, are automatically undone when the garbage collector collects the object,
    /// or the object is disposed. Use <see cref="DetourInfo"/> to get an object which represents the hook without
    /// extending its lifetime.
    /// </remarks>
    public sealed class NativeHook : INativeDetour, IDisposable
    {

        // TODO: reference external xmldoc which describes the shape that a delegate can take, both here and for Hook
        #region Constructor overloads
        /// <summary>
        /// Constructs a <see cref="NativeHook"/> of <paramref name="function"/>, detouring it to <paramref name="hook"/>.
        /// </summary>
        /// <param name="function">A pointer to the native function to hook.</param>
        /// <param name="hook">The delegate which acts as the target of the hook.</param>
        public NativeHook(IntPtr function, Delegate hook)
            : this(function, hook, applyByDefault: true) { }
        /// <summary>
        /// Constructs a <see cref="NativeHook"/> of <paramref name="function"/>, detouring it to <paramref name="hook"/> using the provided config.
        /// </summary>
        /// <param name="function">A pointer to the native function to hook.</param>
        /// <param name="hook">The delegate which acts as the target of the hook.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this detour.</param>
        public NativeHook(IntPtr function, Delegate hook, DetourConfig? config)
            : this(function, hook, config, applyByDefault: true) { }
        /// <summary>
        /// Constructs a <see cref="NativeHook"/> of <paramref name="function"/>, detouring it to <paramref name="hook"/>.
        /// </summary>
        /// <param name="function">A pointer to the native function to hook.</param>
        /// <param name="hook">The delegate which acts as the target of the hook.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public NativeHook(IntPtr function, Delegate hook, bool applyByDefault)
            : this(function, hook, DetourContext.GetDefaultConfig(), applyByDefault) { }
        /// <summary>
        /// Constructs a <see cref="NativeHook"/> of <paramref name="function"/>, detouring it to <paramref name="hook"/> using the provided config.
        /// </summary>
        /// <param name="function">A pointer to the native function to hook.</param>
        /// <param name="hook">The delegate which acts as the target of the hook.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this detour.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public NativeHook(IntPtr function, Delegate hook, DetourConfig? config, bool applyByDefault)
            : this(function, hook, DetourContext.GetDefaultFactory(), config, applyByDefault) { }
        #endregion

        private readonly IDetourFactory factory;
        IDetourFactory IDetourBase.Factory => factory;

        /// <summary>
        /// Gets the <see cref="DetourConfig"/> associated with this <see cref="NativeHook"/>, if any.
        /// </summary>
        public DetourConfig? Config { get; }

        /// <summary>
        /// Gets a pointer to the function which this <see cref="NativeHook"/> hooks.
        /// </summary>
        public IntPtr Function { get; }

        private readonly Delegate hookDel;
        Delegate INativeDetour.Invoker => hookDel;

        private readonly DetourManager.NativeDetourState state;
        private readonly DetourManager.SingleNativeDetourState detour;

        /// <summary>
        /// Constructs a <see cref="NativeHook"/> of <paramref name="function"/>, detouring it to <paramref name="hook"/> using the provided config and detour factory.
        /// </summary>
        /// <param name="function">A pointer to the native function to hook.</param>
        /// <param name="hook">The delegate which acts as the target of the hook.</param>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use to interact with this hook.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this detour.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public NativeHook(IntPtr function, Delegate hook, IDetourFactory factory, DetourConfig? config, bool applyByDefault)
        {
            Helpers.ThrowIfArgumentNull(hook);
            Helpers.ThrowIfArgumentNull(factory);

            Function = function;
            hookDel = hook;
            this.factory = factory;
            Config = config;

            nativeDelType = GetNativeDelegateType(hook.GetType(), out hasOrigParam);

            state = DetourManager.GetNativeDetourState(function);
            detour = new(this);

            if (applyByDefault)
            {
                Apply();
            }
        }

        private readonly Type nativeDelType;
        private readonly bool hasOrigParam;
        Type INativeDetour.NativeDelegateType => nativeDelType;
        bool INativeDetour.HasOrigParam => hasOrigParam;

        private static Type GetNativeDelegateType(Type inDelType, out bool hasOrigParam)
        {
            var sig = MethodSignature.ForMethod(inDelType.GetMethod("Invoke")!, ignoreThis: true);

            // we are kinda guessing here, because we don't know the sig of the method
            if (sig.FirstParameter is { } fst && typeof(Delegate).IsAssignableFrom(fst))
            {
                var fsig = MethodSignature.ForMethod(fst.GetMethod("Invoke")!, ignoreThis: true);
                if (sig.Parameters.Skip(1).SequenceEqual(fsig.Parameters))
                {
                    hasOrigParam = true;
                    return fst;
                }
            }

            hasOrigParam = false;
            return inDelType;
        }

        private void CheckDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        /// <summary>
        /// Applies the hook, if it was not already applied.
        /// </summary>
        public void Apply()
        {
            CheckDisposed();

            var lockTaken = false;
            try
            {
                state.detourLock.Enter(ref lockTaken);
                if (IsApplied)
                    return;
                MMDbgLog.Trace($"Applying NativeHook of 0x{Function:x16}");
                state.AddDetour(detour, !lockTaken);
            }
            finally
            {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        /// <summary>
        /// Undoes the hook, if it was applied.
        /// </summary>
        public void Undo()
        {
            CheckDisposed();

            var lockTaken = false;
            try
            {
                state.detourLock.Enter(ref lockTaken);
                if (!IsApplied)
                    return;
                MMDbgLog.Trace($"Undoing NativeHook from 0x{Function:x16}");
                state.RemoveDetour(detour, !lockTaken);
            }
            finally
            {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        private bool disposedValue;
        /// <summary>
        /// Gets whether or not this hook is valid.
        /// </summary>
        public bool IsValid => !disposedValue;
        /// <summary>
        /// Gets whether or not this hook is currently applied.
        /// </summary>
        public bool IsApplied => detour.IsApplied;
        /// <summary>
        /// Gets the <see cref="NativeDetourInfo"/> representing this hook.
        /// </summary>
        public NativeDetourInfo DetourInfo => state.Info.GetDetourInfo(detour);

        private void Dispose(bool disposing)
        {
            if (!disposedValue && detour is not null)
            {
                detour.IsValid = false;
                if (!(AppDomain.CurrentDomain.IsFinalizingForUnload() || Environment.HasShutdownStarted))
                    Undo();

                disposedValue = true;
            }
        }

        /// <summary>
        /// Cleans up and undoes the hook, if needed.
        /// </summary>
        ~NativeHook()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
