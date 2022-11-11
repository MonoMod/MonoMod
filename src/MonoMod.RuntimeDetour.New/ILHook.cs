using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace MonoMod.RuntimeDetour {

    [CLSCompliant(false)] // TODO: remove when MM.Utils gets CLS compliance annotations
    public class ILHook : IILHook, IDisposable {
        private const bool ApplyByDefault = true;

        // Note: We don't provide all variants with IDetourFactory because providing IDetourFactory is expected to be fairly rare
        #region Constructor overloads
        public ILHook(Expression<Action> source, ILContext.Manipulator manip)
            : this(Helpers.ThrowIfNull(source).Body, manip) { }

        public ILHook(Expression source, ILContext.Manipulator manip)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method, manip) { }

        public ILHook(MethodBase source, ILContext.Manipulator manip)
            : this(source, manip, DetourContext.GetDefaultConfig()) { }

        public ILHook(Expression<Action> source, ILContext.Manipulator manip, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, manip, applyByDefault) { }

        public ILHook(Expression source, ILContext.Manipulator manip, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method, manip, applyByDefault) { }

        public ILHook(MethodBase source, ILContext.Manipulator manip, bool applyByDefault)
            : this(source, manip, DetourContext.GetDefaultConfig(), applyByDefault) { }

        public ILHook(Expression<Action> source, ILContext.Manipulator manip, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, manip, config) { }

        public ILHook(Expression source, ILContext.Manipulator manip, DetourConfig? config)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method, manip, config) { }

        public ILHook(MethodBase source, ILContext.Manipulator manip, DetourConfig? config)
            : this(source, manip, config, ApplyByDefault) { }

        public ILHook(Expression<Action> source, ILContext.Manipulator manip, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, manip, config, applyByDefault) { }

        public ILHook(Expression source, ILContext.Manipulator manip, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  manip, config, applyByDefault) { }

        public ILHook(MethodBase source, ILContext.Manipulator manip, DetourConfig? config, bool applyByDefault)
            : this(source, manip, DetourContext.GetDefaultFactory(), config, applyByDefault) { }
        #endregion

        private readonly IDetourFactory factory;
        IDetourFactory IDetourBase.Factory => factory;

        public MethodBase Method { get; }
        public ILContext.Manipulator Manipulator { get; }
        public DetourConfig? Config { get; }

        ILContext.Manipulator IILHook.Manip => Manipulator;

        private readonly DetourManager.ManagedDetourState state;
        private readonly DetourManager.SingleILHookState hook;

        public ILHook(MethodBase method, ILContext.Manipulator manipulator, IDetourFactory factory, DetourConfig? config, bool applyByDefault) {
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

            if (applyByDefault) {
                Apply();
            }
        }

        private bool disposedValue;
        public bool IsValid => !disposedValue;
        public bool IsApplied => hook.IsApplied;
        public ILHookInfo HookInfo => state.Info.GetILHookInfo(hook);

        private void CheckDisposed() {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        public void Apply() {
            CheckDisposed();

            var lockTaken = false;
            try {
                state.detourLock.Enter(ref lockTaken);
                if (IsApplied)
                    return;
                MMDbgLog.Trace($"Applying ILHook for {Method}");
                state.AddILHook(hook, !lockTaken);
            } finally {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        public void Undo() {
            CheckDisposed();

            var lockTaken = false;
            try {
                state.detourLock.Enter(ref lockTaken);
                if (!IsApplied)
                    return;
                MMDbgLog.Trace($"Undoing ILHook for {Method}");
                state.RemoveILHook(hook, !lockTaken);
            } finally {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue && hook is not null) {
                hook.IsValid = false;
                if (!(AppDomain.CurrentDomain.IsFinalizingForUnload() || Environment.HasShutdownStarted))
                    Undo();

                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }

                disposedValue = true;
            }
        }

        ~ILHook()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
