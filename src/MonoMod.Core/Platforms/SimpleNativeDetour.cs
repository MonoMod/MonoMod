using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A simple native detour from one address to another.
    /// </summary>
    /// <seealso cref="PlatformTriple.CreateSimpleDetour(IntPtr, IntPtr, int, IntPtr)"/>
    /// <seealso cref="PlatformTriple.CreateNativeDetour(IntPtr, IntPtr, int, IntPtr)"/>
    public sealed class SimpleNativeDetour : IDisposable
    {
        private bool disposedValue;
        private readonly PlatformTriple triple;
        private NativeDetourInfo detourInfo;
        private Memory<byte> backup;
        // TODO: replace this with a StrongReference GCHandle so that it's not in the finalization queue simultaneously
        private IDisposable? AllocHandle;

        /// <summary>
        /// Gets the backup data for this detour. This contains the bytes which were originally at the detour location.
        /// </summary>
        public ReadOnlyMemory<byte> DetourBackup => backup;
        /// <summary>
        /// Gets the detour source location.
        /// </summary>
        public IntPtr Source => detourInfo.From;
        /// <summary>
        /// Gets the detour target location.
        /// </summary>
        public IntPtr Destination => detourInfo.To;

        internal SimpleNativeDetour(PlatformTriple triple, NativeDetourInfo detourInfo, Memory<byte> backup, IDisposable? allocHandle)
        {
            this.triple = triple;
            this.detourInfo = detourInfo;
            this.backup = backup;
            AllocHandle = allocHandle;
        }

        // TODO: when this is a NativeDetour, we need to fix up the alt entry point too, if the new patch is bigger

        /// <summary>
        /// Changes the target of this detour to <paramref name="newTarget"/>.,
        /// </summary>
        /// <remarks>
        /// If this <see cref="SimpleNativeDetour"/> was created as a result of <see cref="PlatformTriple.CreateNativeDetour(IntPtr, IntPtr, int, IntPtr)"/>,
        /// then it is not safe to use this method. Because this method may enlarge the detour, it would need to fix up the generated alt entrypoint too, which is
        /// not currently supported. Refer to <see cref="IAltEntryFactory.CreateAlternateEntrypoint(IntPtr, int, out IDisposable?)"/> for more information.
        /// </remarks>
        /// <param name="newTarget">The new target of the detour.</param>
        /// <seealso cref="IArchitecture.ComputeRetargetInfo(NativeDetourInfo, IntPtr, int)"/>
        /// <seealso cref="IArchitecture.GetRetargetBytes(NativeDetourInfo, NativeDetourInfo, Span{byte}, out IDisposable?, out bool, out bool)"/>
        /// <seealso cref="IAltEntryFactory.CreateAlternateEntrypoint(IntPtr, int, out IDisposable?)"/>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "allocHandle is correctly transferred around, as needed")]
        public void ChangeTarget(IntPtr newTarget)
        {
            CheckDisposed();

            MMDbgLog.Trace($"Retargeting simple detour 0x{Source:x16} => 0x{Destination:x16} to target 0x{newTarget:x16}");

            // This is effectively the same as PlatformTriple.CreateSimpleDetour, only using the underlying retargeting API

            var retarget = triple.Architecture.ComputeRetargetInfo(detourInfo, newTarget, detourInfo.Size);

            Span<byte> retargetBytes = stackalloc byte[retarget.Size];

            var wroteBytes = triple.Architecture.GetRetargetBytes(detourInfo, retarget, retargetBytes, out var alloc, out var repatch, out var disposeOldAlloc);

            // this is the major place where logic diverges
            // notably, we want to do nearly completely different things if we need to repatch versus not
            if (repatch)
            {
                // the retarget requires re-patching the source body
                Helpers.DAssert(retarget.Size == wroteBytes);

                byte[]? newBackup = null;
                if (retarget.Size > backup.Length)
                {
                    // the retarget is actually larger than the old detour, so we need to allocate a new backup array and do some shenanigans to keep it consistent
                    newBackup = new byte[retarget.Size];
                }
                // if the retarget is less than or equal to the size of our backup, then we already have the backup that we need and don't need to do anything more

                triple.System.PatchData(PatchTargetKind.Executable, Source, retargetBytes, newBackup);

                if (newBackup is not null)
                {
                    // this means that the retarget is larger, so we want to copy in our old backup
                    backup.Span.CopyTo(newBackup); // this will overwrite the existing patch in this backup
                    backup = newBackup;
                }
            }

            // we always want to replace our DetourInfo
            detourInfo = retarget;
            // and we want to swap the old and new allocations, disposing the old only if disposeOldAlloc
            (alloc, AllocHandle) = (AllocHandle, alloc);
            if (disposeOldAlloc)
            {
                alloc?.Dispose();
            }
        }

        /// <summary>
        /// Undoes this detour. After this is called, the object is disposed, and may not be used.
        /// </summary>
        public void Undo()
        {
            CheckDisposed();
            UndoCore(true);
        }

        private void CheckDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(SimpleNativeDetour));
        }

        private void UndoCore(bool disposing)
        {
            MMDbgLog.Trace($"Undoing simple detour 0x{Source:x16} => 0x{Destination:x16}");
            // literally just patch again, but the other direction
            triple.System.PatchData(PatchTargetKind.Executable, Source, DetourBackup.Span, default);
            if (disposing)
            {
                Cleanup();
            }
            disposedValue = true;
        }

        private void Cleanup()
        {
            AllocHandle?.Dispose();
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                UndoCore(disposing);

                disposedValue = true;
            }
        }

        /// <summary>
        /// Undoes and cleans up this detour.
        /// </summary>
        ~SimpleNativeDetour()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        /// <summary>
        /// Undoes and cleans up this detour.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
