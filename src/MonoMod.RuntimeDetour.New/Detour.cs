using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Core.Utils;
using MonoMod.RuntimeDetour.Utils;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

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

        private readonly IDetourFactory factory;
        public PlatformTriple Platform { get; }

        public DetourConfig? Config { get; }

        public MethodBase Source { get; }
        public MethodInfo Target { get; }

        MethodInfo IDetour.InvokeTarget => Target;

        private readonly MethodInfo trampoline;
        private bool disposedValue;

        MethodBase IDetour.NextTrampoline => trampoline;

        private object? managerData;
        object? IDetour.ManagerData { get => managerData; set => managerData = value; }

        private readonly DetourManager.DetourState state;

        public Detour(MethodBase source, MethodInfo target, DetourConfig? config, bool applyByDefault) {
            Config = config;
            Platform = PlatformTriple.Current;
            factory = DetourFactory.Current;

            Source = Platform.GetIdentifiable(source);
            Target = target;

            trampoline = TrampolinePool.Rent(MethodSignature.ForMethod(source));

            state = DetourManager.GetDetourState(source);

            if (applyByDefault) {
                Apply();
            }
        }

        public bool IsValid => !disposedValue;

        private bool isApplied;
        public bool IsApplied => Volatile.Read(ref isApplied);

        private void CheckDisposed() {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        public void Apply() {
            CheckDisposed();
            if (IsApplied)
                return;
            Volatile.Write(ref isApplied, true);
            state.AddDetour(factory, this);
        }

        public void Undo() {
            CheckDisposed();
            if (!IsApplied)
                return;
            Volatile.Write(ref isApplied, value: false);
            state.RemoveDetour(factory, this);
        }

        // TODO: is there something better we can do here? something that maybe lets us reuse trampolines, or generally avoid
        // codegen that doesn't need to happen?

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
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
        public T GenerateTrampoline<T>() where T : Delegate {
            return GenerateTrampoline().CreateDelegate<T>();
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }

                Undo();
                GC.ReRegisterForFinalize(trampoline);
                TrampolinePool.Return(trampoline);
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
