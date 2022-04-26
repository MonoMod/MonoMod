using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.Core.Platforms {

    public class PlatformTripleDetourFactory : IDetourFactory {

        private readonly PlatformTriple triple;

        public PlatformTripleDetourFactory(PlatformTriple triple) {
            this.triple = triple;
        }

        public FeatureFlags SupportedFeatures => triple.SupportedFeatures;

        public ICoreDetour CreateDetour(CreateDetourRequest request) {
            Helpers.ThrowIfNull(request.Source);
            Helpers.ThrowIfNull(request.Target);

            if (!triple.TryDisableInlining(request.Source))
                MMDbgLog.Log($"Could not disable inlining of method {request.Source.GetID()}; detours may not be reliable");

            var detour = new Detour(triple, request.Source, request.Target);
            if (request.ApplyByDefault) {
                detour.Apply();
            }
            return detour;
        }

        private sealed class Detour : ICoreDetour {

            private readonly PlatformTriple triple;
            private readonly MethodBase realTarget;

            public Detour(PlatformTriple triple, MethodBase src, MethodBase dst) {
                this.triple = triple;
                Source = src;
                Target = dst;

                realTarget = triple.GetRealDetourTarget(src, dst);
            }

            public MethodBase Source { get; }

            public MethodBase Target { get; }

            private IDisposable? srcPin;
            private IDisposable? dstPin;
            private NativeDetour? nativeDetour;

            [MemberNotNullWhen(true, nameof(nativeDetour))]
            public bool IsApplied => nativeDetour is not null;

            public bool IsAttached { get; private set; } = true;

            public void Apply() {
                if (IsApplied)
                    throw new InvalidOperationException("Cannot apply a detour which is already applied");

                srcPin = triple.PinMethodIfNeeded(Source);
                dstPin = triple.PinMethodIfNeeded(realTarget);

                var from = triple.GetNativeMethodBody(Source);
                // we don't really want to follow the method thunks for our target, because some runtimes may recompile it
                // and we want to get that benefit
                var to = triple.GetNativeMethodBody(realTarget, followThunks: false);

                nativeDetour = triple.CreateNativeDetour(from, to);
            }

            public void Undo() {
                if (!IsApplied)
                    throw new InvalidOperationException("Cannot undo a detour which is not applied");
                UndoCore();
            }

            private void UndoCore() {
                Interlocked.Exchange(ref nativeDetour, null)?.Undo();
                Interlocked.Exchange(ref srcPin, null)?.Dispose();
                Interlocked.Exchange(ref dstPin, null)?.Dispose();
            }

            public void Attach() {
                if (IsAttached)
                    throw new InvalidOperationException("Cannot attach an already attached detour");
                nativeDetour?.MakeAutomatic();
                IsAttached = false;
            }

            public void Detatch() {
                if (!IsAttached)
                    throw new InvalidOperationException("Cannot detatch an already detached detour");
                nativeDetour?.MakeManualOnly();
                IsAttached = true;
            }

            #region IDisposable implementation
            private bool disposedValue;

            private void Dispose(bool disposing) {
                if (!disposedValue) {
                    if (IsAttached) {
                        // we're attached, we need to undo the detour if its applied
                        UndoCore();
                    } else {
                        // otherwise, we need to ensure the pins live forever, and make the native detour ManualOnly and dispose it
                        _ = GCHandle.Alloc(Interlocked.Exchange(ref srcPin, null));
                        _ = GCHandle.Alloc(Interlocked.Exchange(ref dstPin, null));
                        var detour = Interlocked.Exchange(ref nativeDetour, null);
                        if (detour is not null && disposing) {
                            detour.Dispose();
                        }
                    }

                    disposedValue = true;
                }
            }

            ~Detour() {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: false);
            }

            public void Dispose() {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}
