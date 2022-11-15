using Mono.Cecil;
using MonoMod.Core;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour {
    public sealed class NativeHook : INativeDetour, IDisposable {


        #region Constructor overloads
        public NativeHook(IntPtr function, Delegate hook)
            : this(function, hook, applyByDefault: true) { }
        public NativeHook(IntPtr function, Delegate hook, DetourConfig? config)
            : this(function, hook, config, applyByDefault: true) { }
        public NativeHook(IntPtr function, Delegate hook, bool applyByDefault)
            : this(function, hook, DetourContext.GetDefaultConfig(), applyByDefault) { }
        public NativeHook(IntPtr function, Delegate hook, DetourConfig? config, bool applyByDefault)
            : this(function, hook, DetourContext.GetDefaultFactory(), config, applyByDefault) { }
        #endregion

        private readonly IDetourFactory factory;
        IDetourFactory IDetourBase.Factory => factory;

        public DetourConfig? Config { get; }

        public IntPtr Function { get; }

        private readonly Delegate hookDel;
        Delegate INativeDetour.Invoker => hookDel;

        private readonly DetourManager.NativeDetourState state;
        private readonly DetourManager.SingleNativeDetourState detour;

        public NativeHook(IntPtr function, Delegate hook, IDetourFactory factory, DetourConfig? config, bool applyByDefault) {
            Helpers.ThrowIfArgumentNull(hook);
            Helpers.ThrowIfArgumentNull(factory);

            Function = function;
            hookDel = hook;
            this.factory = factory;
            Config = config;

            nativeDelType = GetNativeDelegateType(hook.GetType(), out hasOrigParam);

            state = DetourManager.GetNativeDetourState(function);
            detour = new(this);

            if (applyByDefault) {
                Apply();
            }
        }

        private readonly Type nativeDelType;
        private readonly bool hasOrigParam;
        Type INativeDetour.NativeDelegateType => nativeDelType;
        bool INativeDetour.HasOrigParam => hasOrigParam;

        private static Type GetNativeDelegateType(Type inDelType, out bool hasOrigParam) {
            var sig = MethodSignature.ForMethod(inDelType.GetMethod("Invoke")!);

            // we are kinda guessing here, because we don't know the sig of the method
            if (sig.FirstParameter is { } fst && typeof(Delegate).IsAssignableFrom(fst)) {
                var fsig = MethodSignature.ForMethod(fst.GetMethod("Invoke")!);
                if (sig.Parameters.Skip(1).SequenceEqual(fsig.Parameters)) {
                    hasOrigParam = true;
                    return fst;
                }
            }

            hasOrigParam = false;
            return inDelType;
        }

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
                MMDbgLog.Trace($"Applying NativeHook of 0x{Function:x16}");
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
                MMDbgLog.Trace($"Undoing NativeHook from 0x{Function:x16}");
                state.RemoveDetour(detour, !lockTaken);
            } finally {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        private bool disposedValue;
        public bool IsValid => !disposedValue;
        public bool IsApplied => detour.IsApplied;
        public NativeDetourInfo DetourInfo => state.Info.GetDetourInfo(detour);

        private void Dispose(bool disposing) {
            if (!disposedValue && detour is not null) {
                detour.IsValid = false;
                if (!(AppDomain.CurrentDomain.IsFinalizingForUnload() || Environment.HasShutdownStarted))
                    Undo();

                disposedValue = true;
            }
        }

        ~NativeHook() {
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
