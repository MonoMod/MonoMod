using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms {
    public sealed class NativeDetour : IDisposable {
        private bool disposedValue;
        private readonly PlatformTriple triple;
        private readonly IDisposable? AllocHandle;

        public ReadOnlyMemory<byte> DetourBackup { get; }
        public IntPtr Source { get; }
        public IntPtr Destination { get; }

        private bool isAutoUndone = true;
        public bool IsAutoUndone => isAutoUndone;

        internal NativeDetour(PlatformTriple triple, IntPtr src, IntPtr dest, ReadOnlyMemory<byte> backup, IDisposable? allocHandle) {
            this.triple = triple;
            Source = src;
            Destination = dest;
            DetourBackup = backup;
            AllocHandle = allocHandle;
        }

        public void MakeManualOnly() {
            CheckDisposed();
            isAutoUndone = false;
        }

        public void MakeAutomatic() {
            CheckDisposed();
            isAutoUndone = true;
        }

        /// <summary>
        /// Undoes this detour. After this is called, the object is disposed, and may not be used.
        /// </summary>
        public void Undo() {
            CheckDisposed();
            UndoCore(true);
        }

        private void CheckDisposed() {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(NativeDetour));
        }

        private void UndoCore(bool disposing) {
            // literally just patch again, but the other direction
            triple.System.PatchExecutableData(Source, DetourBackup.Span, default);
            if (disposing) {
                AllocHandle?.Dispose();
            }
        }

        private void Dispose(bool disposing) {
            if (!disposedValue) {
                if (isAutoUndone) {
                    UndoCore(disposing);
                } else {
                    // create a gc handle to the allocHandle
                    _ = GCHandle.Alloc(AllocHandle);
                }

                disposedValue = true;
            }
        }

        ~NativeDetour() {
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
