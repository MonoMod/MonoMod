using MonoMod.Core.Platforms;
using MonoMod.Core.Utils;
using MonoMod.RuntimeDetour.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MonoMod.RuntimeDetour {

    public class Detour : IDetour, IDisposable {

        public const bool ApplyByDefault = true;

        #region Constructor overloads
        public Detour(Expression<Action> source, Expression<Action> target)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body) { }

        public Detour(Expression source, Expression target)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method) { }

        public Detour(MethodBase source, MethodInfo target)
            : this(source, target, null) { }

        public Detour(Expression<Action> source, Expression<Action> target, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, applyByDefault) { }

        public Detour(Expression source, Expression target, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, applyByDefault) { }

        public Detour(MethodBase source, MethodInfo target, bool applyByDefault)
            : this(source, target, null, applyByDefault) { }

        public Detour(Expression<Action> source, Expression<Action> target, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, config) { }

        public Detour(Expression source, Expression target, DetourConfig? config)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, config) { }

        public Detour(MethodBase source, MethodInfo target, DetourConfig? config)
            : this(source, target, config, ApplyByDefault) { }

        public Detour(Expression<Action> source, Expression<Action> target, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, config, applyByDefault) { }

        public Detour(Expression source, Expression target, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method, 
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, config, applyByDefault) { }
        #endregion

        public PlatformTriple Platform { get; }

        public DetourConfig? Config { get; }

        public MethodBase Source { get; }
        public MethodInfo Target { get; }

        MethodInfo IDetour.InvokeTarget => Target;

        private readonly MethodInfo trampoline;
        private bool disposedValue;

        MethodBase IDetour.NextTrampoline => trampoline;

        private readonly DetourManager.DetourState state;

        public Detour(MethodBase source, MethodInfo target, DetourConfig? config, bool applyByDefault) {
            Config = config;
            Platform = PlatformTriple.Current;

            Source = Platform.GetIdentifiable(source);
            Target = target;

            trampoline = TrampolinePool.Rent(MethodSignature.ForMethod(source));

            state = DetourManager.GetDetourState(source);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                TrampolinePool.Return(trampoline);
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~Detour()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
