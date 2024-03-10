using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace MonoMod.RuntimeDetour
{

    /// <summary>
    /// A detour type which allows the manipulation of the IL of a method, instead of merely changing its target.
    /// </summary>
    /// <remarks>
    /// <see cref="ILHook"/>s, like other kinds of detours, are automatically undone when the garbage collector collects the object,
    /// or the object is disposed. Use <see cref="HookInfo"/> to get an object which represents the hook without
    /// extending its lifetime.
    /// </remarks>
    [CLSCompliant(false)] // TODO: remove when MM.Utils gets CLS compliance annotations
    public sealed class ILHook : IILHook, IDisposable
    {
        private const bool ApplyByDefault = true;

        // Note: We don't provide all variants with IDetourFactory because providing IDetourFactory is expected to be fairly rare
        #region Constructor overloads
        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        public ILHook(Expression<Action> source, ILContext.Manipulator manip)
            : this(Helpers.ThrowIfNull(source).Body, manip) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        public ILHook(Expression source, ILContext.Manipulator manip)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, manip) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the provided method using the provided manipulator.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        public ILHook(MethodBase source, ILContext.Manipulator manip)
            : this(source, manip, DetourContext.GetDefaultConfig()) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(Expression<Action> source, ILContext.Manipulator manip, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, manip, applyByDefault) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(Expression source, ILContext.Manipulator manip, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, manip, applyByDefault) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the provided method using the provided manipulator.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(MethodBase source, ILContext.Manipulator manip, bool applyByDefault)
            : this(source, manip, DetourContext.GetDefaultConfig(), applyByDefault) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator,
        /// using the specified <see cref="DetourContext"/>.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        public ILHook(Expression<Action> source, ILContext.Manipulator manip, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, manip, config) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator and <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        public ILHook(Expression source, ILContext.Manipulator manip, DetourConfig? config)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, manip, config) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the provided method using the provided manipulator and <see cref="DetourConfig"/>
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        public ILHook(MethodBase source, ILContext.Manipulator manip, DetourConfig? config)
            : this(source, manip, config, ApplyByDefault) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator,
        /// using the specified <see cref="DetourContext"/>.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(Expression<Action> source, ILContext.Manipulator manip, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, manip, config, applyByDefault) { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the method specified by the provided expression tree using the provided manipulator,
        /// using the specified <see cref="DetourContext"/>.
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(Expression source, ILContext.Manipulator manip, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  manip, config, applyByDefault)
        { }

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the provided method using the provided manipulator and <see cref="DetourConfig"/>
        /// </summary>
        /// <param name="source">The method to modify the IL of.</param>
        /// <param name="manip">The manipulator to use to modify the method's IL.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(MethodBase source, ILContext.Manipulator manip, DetourConfig? config, bool applyByDefault)
            : this(source, manip, DetourContext.GetDefaultFactory(), config, applyByDefault) { }
        #endregion

        private readonly IDetourFactory factory;
        IDetourFactory IDetourBase.Factory => factory;

        /// <summary>
        /// Gets the method which this <see cref="ILHook"/> modifies.
        /// </summary>
        public MethodBase Method { get; }
        /// <summary>
        /// Gets the <see cref="ILContext.Manipulator"/> used by this <see cref="ILHook"/> to modify <see cref="Method"/>'s IL.
        /// </summary>
        public ILContext.Manipulator Manipulator { get; }
        /// <summary>
        /// Gets the <see cref="DetourConfig"/> used by this <see cref="ILHook"/>, if any.
        /// </summary>
        public DetourConfig? Config { get; }

        ILContext.Manipulator IILHook.Manip => Manipulator;

        private readonly DetourManager.ManagedDetourState state;
        private readonly DetourManager.SingleILHookState hook;

        /// <summary>
        /// Constructs an <see cref="ILHook"/> for the provided method using the provided manipulator and <see cref="DetourConfig"/>
        /// </summary>
        /// <param name="method">The method to modify the IL of.</param>
        /// <param name="manipulator">The manipulator to use to modify the method's IL.</param>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use when manipulating this <see cref="ILHook"/>.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="ILHook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public ILHook(MethodBase method, ILContext.Manipulator manipulator, IDetourFactory factory, DetourConfig? config, bool applyByDefault)
        {
            Helpers.ThrowIfArgumentNull(method);
            Helpers.ThrowIfArgumentNull(manipulator);
            Helpers.ThrowIfArgumentNull(factory);

            Method = PlatformTriple.Current.GetIdentifiable(method);
            Manipulator = manipulator;
            Config = config;
            this.factory = factory;

            MMDbgLog.Trace($"Creating ILHook for {Method}");

            state = DetourManager.GetDetourState(method);
            hook = new(this);

            if (applyByDefault)
            {
                Apply();
            }
        }

        private bool disposedValue;
        /// <summary>
        /// Gets whether or not this <see cref="ILHook"/> is valid and can be used.
        /// </summary>
        public bool IsValid => !disposedValue;
        /// <summary>
        /// Gets whether or not this <see cref="ILHook"/> is applied.
        /// </summary>
        public bool IsApplied => hook.IsApplied;
        /// <summary>
        /// Gets the <see cref="ILHookInfo"/> for this <see cref="ILHook"/>.
        /// </summary>
        public ILHookInfo HookInfo => state.Info.GetILHookInfo(hook);

        private void CheckDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        /// <summary>
        /// Applies this <see cref="ILHook"/> if it was not already applied.
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
                MMDbgLog.Trace($"Applying ILHook for {Method}");
                state.AddILHook(hook, !lockTaken);
            }
            finally
            {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        /// <summary>
        /// Undoes this <see cref="ILHook"/> if it was applied.
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
                MMDbgLog.Trace($"Undoing ILHook for {Method}");
                state.RemoveILHook(hook, !lockTaken);
            }
            finally
            {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue && hook is not null)
            {
                hook.IsValid = false;
                if (!(AppDomain.CurrentDomain.IsFinalizingForUnload() || Environment.HasShutdownStarted))
                    Undo();

                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Cleans up and undoes the hook, if needed.
        /// </summary>
        ~ILHook()
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
