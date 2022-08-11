using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace MonoMod.RuntimeDetour {

    public class Detour : IDetour, IDisposable {

        public const bool ApplyByDefault = true;

        // Note: We don't provide all variants with IDetourFactory because providing IDetourFactory is expected to be fairly rare
        #region Constructor overloads
        public Detour(Expression<Action> source, Expression<Action> target)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body) { }

        public Detour(Expression source, Expression target)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method) { }

        public Detour(MethodBase source, MethodInfo target)
            : this(source, target, DetourContext.GetDefaultConfig()) { }

        public Detour(Expression<Action> source, Expression<Action> target, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, applyByDefault) { }

        public Detour(Expression source, Expression target, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, applyByDefault) { }

        public Detour(MethodBase source, MethodInfo target, bool applyByDefault)
            : this(source, target, DetourContext.GetDefaultConfig(), applyByDefault) { }

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

        public Detour(MethodBase source, MethodInfo target, DetourConfig? config, bool applyByDefault)
            : this(source, target, DetourContext.GetDefaultFactory(), config, applyByDefault) { }
        #endregion

        private readonly IDetourFactory factory;
        IDetourFactory IDetour.Factory => factory;

        public DetourConfig? Config { get; }

        public MethodBase Source { get; }
        public MethodInfo Target { get; }

        MethodInfo IDetour.InvokeTarget => Target;

        private readonly MethodInfo trampoline;
        MethodBase IDetour.NextTrampoline => trampoline;

        private readonly DetourManager.DetourState state;
        private readonly DetourManager.SingleDetourState detour;

        public Detour(MethodBase source, MethodInfo target, IDetourFactory factory, DetourConfig? config, bool applyByDefault) {
            Helpers.ThrowIfArgumentNull(source);
            Helpers.ThrowIfArgumentNull(target);
            Helpers.ThrowIfArgumentNull(factory);

            Config = config;
            this.factory = factory;

            Source = source;
            Target = target;

            if (!target.IsStatic) {
                throw new ArgumentException("Target method is not static", nameof(target));
            }

            var srcSig = MethodSignature.ForMethod(source);
            var dstSig = MethodSignature.ForMethod(target);

            if (!srcSig.IsCompatibleWith(dstSig)) {
                throw new ArgumentException($"Target method is not compatible with source method (src: {srcSig}, dst: {dstSig})");
            }

            trampoline = TrampolinePool.Rent(srcSig);

            state = DetourManager.GetDetourState(source);
            detour = new(this);

            if (applyByDefault) {
                Apply();
            }
        }

        private bool disposedValue;
        public bool IsValid => !disposedValue;

        public bool IsApplied => detour.IsApplied;

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
                state.AddDetour(detour, !lockTaken);
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
                state.RemoveDetour(detour, !lockTaken);
            } finally {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        // TODO: is there something better we can do here? something that maybe lets us reuse trampolines, or generally avoid
        // codegen that doesn't need to happen?

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
        /// <remarks>
        /// If this method is not called within a call to the detoured method, the behaviour of the generated trampoline is undefined.
        /// It will likely work as long as there isn't a concurrent update to the detour chain, but there are no protections against
        /// that going wrong.
        /// </remarks>
        public MethodBase GenerateTrampoline() {
            /*MethodBase remoteTrampoline = OnGenerateTrampoline?.InvokeWhileNull<MethodBase>(this, signature);
            if (remoteTrampoline != null)
                return remoteTrampoline;

            if (signature == null)
                signature = Target;*/

            var sig = MethodSignature.ForMethod(Source);

            // Note: It'd be more performant to skip this step and just return the "chained trampoline."
            // Unfortunately, it'd allow a third party to break the Detour trampoline chain, among other things.
            // Instead, we create and return a DynamicMethod calling the "chained trampoline."

            // TODO: this likely isn't safe, because the trampolines will be reused
            using (var dmd = sig.CreateDmd($"Trampoline<{sig}>?{GetHashCode()}")) {
                ILProcessor il = dmd.GetILProcessor();

                for (var i = 0; i < 32; i++) {
                    // Prevent mono from inlining the DynamicMethod.
                    il.Emit(OpCodes.Nop);
                }

                // Jmp and older versions of mono don't work well together.
                // il.Emit(OpCodes.Jmp, _ChainedTrampoline);

                // Manually call the target method instead.
                for (var i = 0; i < sig.ParameterCount; i++)
                    il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Call, trampoline);
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }
        }

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
        /// <remarks>
        /// If this method is not called within a call to the detoured method, the behaviour of the generated trampoline is undefined.
        /// It will likely work as long as there isn't a concurrent update to the detour chain, but there are no protections against
        /// that going wrong.
        /// </remarks>
        public T GenerateTrampoline<T>() where T : Delegate {
            return GenerateTrampoline().CreateDelegate<T>();
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                detour.IsValid = false;
                Undo();

                if (disposing) {
                    TrampolinePool.Return(trampoline);
                }

                disposedValue = true;
            }
        }

        ~Detour()
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
