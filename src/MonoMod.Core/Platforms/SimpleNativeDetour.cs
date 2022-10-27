using System;
using MonoMod.Utils;

namespace MonoMod.Core.Platforms {
    public sealed class SimpleNativeDetour : IDisposable {
        private bool disposedValue;
        private readonly PlatformTriple triple;
        private NativeDetourInfo detourInfo;
        private Memory<byte> backup;
        // TODO: replace this with a StrongReference GCHandle so that it's not in the finalization queue simultaneously
        private IDisposable? AllocHandle;

        public ReadOnlyMemory<byte> DetourBackup => backup;
        public IntPtr Source => detourInfo.From;
        public IntPtr Destination => detourInfo.To;

        internal SimpleNativeDetour(PlatformTriple triple, NativeDetourInfo detourInfo, Memory<byte> backup, IDisposable? allocHandle) {
            this.triple = triple;
            this.detourInfo = detourInfo;
            this.backup = backup;
            AllocHandle = allocHandle;
        }

        public void ChangeTarget(IntPtr newTarget) {
            CheckDisposed();

            MMDbgLog.Trace($"Retargeting simple detour 0x{Source:x16} => 0x{Destination:x16} to target 0x{newTarget:x16}");

            // This is effectively the same as PlatformTriple.CreateSimpleDetour, only using the underlying retargeting API

            var retarget = triple.Architecture.ComputeRetargetInfo(detourInfo, newTarget, detourInfo.Size);

            Span<byte> retargetBytes = stackalloc byte[retarget.Size];

            var wroteBytes = triple.Architecture.GetRetargetBytes(detourInfo, retarget, retargetBytes, out var alloc, out var repatch, out var disposeOldAlloc);

            // this is the major place where logic diverges
            // notably, we want to do nearly completely different things if we need to repatch versus not
            if (repatch) {
                // the retarget requires re-patching the source body
                Helpers.DAssert(retarget.Size == wroteBytes);

                byte[]? newBackup = null;
                if (retarget.Size > backup.Length) {
                    // the retarget is actually larger than the old detour, so we need to allocate a new backup array and do some shenanigans to keep it consistent
                    newBackup = new byte[retarget.Size];
                }
                // if the retarget is less than or equal to the size of our backup, then we already have the backup that we need and don't need to do anything more

                triple.System.PatchData(PatchTargetKind.Executable, Source, retargetBytes, newBackup);

                if (newBackup is not null) {
                    // this means that the retarget is larger, so we want to copy in our old backup
                    backup.Span.CopyTo(newBackup); // this will overwrite the existing patch in this backup
                    backup = newBackup;
                }
            }

            // we always want to replace our DetourInfo
            detourInfo = retarget;
            // and we want to swap the old and new allocations, disposing the old only if disposeOldAlloc
            (alloc, AllocHandle) = (AllocHandle, alloc);
            if (disposeOldAlloc) {
                alloc?.Dispose();
            }
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
                throw new ObjectDisposedException(nameof(SimpleNativeDetour));
        }

        private void UndoCore(bool disposing) {
            MMDbgLog.Trace($"Undoing simple detour 0x{Source:x16} => 0x{Destination:x16}");
            // literally just patch again, but the other direction
            triple.System.PatchData(PatchTargetKind.Executable, Source, DetourBackup.Span, default);
            if (disposing) {
                AllocHandle?.Dispose();
            }
            disposedValue = true;
        }

        private void Dispose(bool disposing) {
            if (!disposedValue) {
                UndoCore(disposing);

                disposedValue = true;
            }
        }

        ~SimpleNativeDetour() {
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
