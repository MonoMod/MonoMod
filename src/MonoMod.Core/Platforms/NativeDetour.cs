using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms {
    public sealed class NativeDetour : IDisposable {
        private bool disposedValue;
        private readonly PlatformTriple triple;

        public ReadOnlyMemory<byte> DetourBackup { get; }
        public IntPtr Source { get; }
        public IntPtr Destination { get; }

        private bool isAutoUndone = true;
        public bool IsAutoUndone => isAutoUndone;

        internal NativeDetour(PlatformTriple triple, IntPtr src, IntPtr dest, ReadOnlyMemory<byte> backup) {
            this.triple = triple;
            Source = src;
            Destination = dest;
            DetourBackup = backup;
        }

        public void MakeManualOnly() {
            isAutoUndone = false;
        }

        public void MakeAutomatic() {
            isAutoUndone = true;
        }

        public void Undo() {
            // literally just patch again, but the other direction
            triple.System.PatchExecutableData(Source, DetourBackup.Span, default);
        }

        private void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }

                if (isAutoUndone) {
                    Undo();
                }
                disposedValue = true;
            }
        }

        ~NativeDetour()
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
