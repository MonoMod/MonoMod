using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public class ILHook : IILHook, IDisposable {

        private readonly IDetourFactory factory;
        IDetourFactory IILHook.Factory => factory;

        public MethodBase Method { get; }
        public ILContext.Manipulator Manipulator { get; }
        public DetourConfig? Config { get; }


        ILContext.Manipulator IILHook.Manip => Manipulator;

        private object? managerData;
        object? IILHook.ManagerData { get => managerData; set => managerData = value; }

        private readonly DetourManager.DetourState state;

        public ILHook(MethodBase method, ILContext.Manipulator manipulator, IDetourFactory factory, DetourConfig? config, bool applyByDefault) {
            Helpers.ThrowIfArgumentNull(method);
            Helpers.ThrowIfArgumentNull(manipulator);
            Helpers.ThrowIfArgumentNull(factory);

            Method = method;
            Manipulator = manipulator;
            Config = config;
            this.factory = factory;

            state = DetourManager.GetDetourState(method);

            if (applyByDefault) {
                Apply();
            }
        }


        private bool isApplied;
        public bool IsApplied => Volatile.Read(ref isApplied);


        private bool disposedValue;
        public bool IsValid => !disposedValue;

        private void CheckDisposed() {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        public void Apply() {
            CheckDisposed();
            if (IsApplied)
                return;
            Volatile.Write(ref isApplied, true);
            state.AddILHook(this);
        }

        public void Undo() {
            CheckDisposed();
            if (!IsApplied)
                return;
            Volatile.Write(ref isApplied, value: false);
            state.RemoveILHook(this);
        }


        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
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
