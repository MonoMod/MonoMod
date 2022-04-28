using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            private readonly object sync = new();
            private readonly PlatformTriple triple;
            private readonly MethodBase realTarget;
            private readonly CompileMethodHook? compileMethodHook;

            // TODO: don't have a OnMethodCompiled subscription for each Detour
            public Detour(PlatformTriple triple, MethodBase src, MethodBase dst) {
                this.triple = triple;
                Source = src;
                Target = dst;
                nativeDetour = new(null);

                realTarget = triple.GetRealDetourTarget(src, dst);

                if (triple.SupportedFeatures.Has(RuntimeFeature.CompileMethodHook)) {
                    compileMethodHook = new CompileMethodHook(sync, triple, src, realTarget, nativeDetour);
                    triple.Runtime.OnMethodCompiled += compileMethodHook.OnMethodCompiled;
                }
            }

            private sealed class CompileMethodHook {
                private readonly object sync;
                private readonly PlatformTriple triple;
                private readonly MethodBase src;
                private readonly MethodBase target;
                private readonly StrongBox<NativeDetour?> nativeDetour;
                public bool IsDetourReleased;

                public CompileMethodHook(object sync, PlatformTriple triple, MethodBase src, MethodBase target, StrongBox<NativeDetour?> nativeDetour) {
                    this.sync = sync;
                    this.triple = triple;
                    this.src = src;
                    this.target = target;
                    this.nativeDetour = nativeDetour;
                }

                public void OnMethodCompiled(MethodBase? method, IntPtr codeStart, ulong codeSize) {
                    if (method is null)
                        return;
                    if (nativeDetour.Value is null)
                        return;
                    if (method != src)
                        return;

                    NativeDetour? oldDetour;
                    lock (sync) {
                        var to = triple.GetNativeMethodBody(target, followThunks: false);
                        var newDetour = triple.CreateNativeDetour(codeStart, to, detourMaxSize: (int) codeSize);
                        oldDetour = Interlocked.Exchange(ref nativeDetour.Value, newDetour);
                        if (Volatile.Read(ref IsDetourReleased)) {
                            newDetour?.MakeManualOnly();
                        }
                    }

                    // we want to make sure our old detour is set to Automatic, then just release it to be cleaned up on the next GC, which
                    // we'll presume is *after* the new method body is in place
                    if (oldDetour is { } old && !old.IsAutoUndone) {
                        old.MakeAutomatic();
                    }

                    GC.KeepAlive(oldDetour); // after this point, we'll consider it dead
                }
            }

            public MethodBase Source { get; }

            public MethodBase Target { get; }

            // These fields are disposed if needed, just through some more indirections than is typical.
#pragma warning disable CA2213 // Disposable fields should be disposed
            private IDisposable? srcPin;
            private IDisposable? dstPin;
            private readonly StrongBox<NativeDetour?> nativeDetour;
#pragma warning restore CA2213 // Disposable fields should be disposed

            public bool IsApplied => nativeDetour.Value is not null;

            public bool IsAttached { get; private set; } = true;

            public void Apply() {
                lock (sync) {
                    if (IsApplied)
                        throw new InvalidOperationException("Cannot apply a detour which is already applied");

                    srcPin = triple.PinMethodIfNeeded(Source);
                    dstPin = triple.PinMethodIfNeeded(realTarget);

                    var from = triple.GetNativeMethodBody(Source);
                    // we don't really want to follow the method thunks for our target, because some runtimes may recompile it
                    // and we want to get that benefit
                    var to = triple.GetNativeMethodBody(realTarget, followThunks: false);

                    nativeDetour.Value = triple.CreateNativeDetour(from, to);
                }
            }

            public void Undo() {
                lock (sync) {
                    if (!IsApplied)
                        throw new InvalidOperationException("Cannot undo a detour which is not applied");
                    UndoCore();
                }
            }

            private void UndoCore() {
                Interlocked.Exchange(ref nativeDetour.Value, null)?.Undo();
                Interlocked.Exchange(ref srcPin, null)?.Dispose();
                Interlocked.Exchange(ref dstPin, null)?.Dispose();
            }

            public void Attach() {
                lock (sync) {
                    if (IsAttached)
                        throw new InvalidOperationException("Cannot attach an already attached detour");
                    nativeDetour.Value?.MakeAutomatic();
                    IsAttached = true;
                    if (compileMethodHook is { } cmh) {
                        Volatile.Write(ref cmh.IsDetourReleased, false);
                    }
                }
            }

            public void Detatch() {
                lock (sync) {
                    if (!IsAttached)
                        throw new InvalidOperationException("Cannot detatch an already detached detour");
                    nativeDetour.Value?.MakeManualOnly();
                    if (compileMethodHook is { } cmh) {
                        Volatile.Write(ref cmh.IsDetourReleased, true);
                    }
                }
            }

            #region IDisposable implementation
            private bool disposedValue;

            private void Dispose(bool disposing) {
                if (!disposedValue) {
                    if (IsAttached) {
                        // we're attached, we need to undo the detour if its applied
                        if (compileMethodHook is not null) {
                            // if this detour applied a callback, remove it
                            triple.Runtime.OnMethodCompiled -= compileMethodHook.OnMethodCompiled;
                        }
                        UndoCore();
                    } else {
                        // otherwise, we need to ensure the pins live forever, and make the native detour ManualOnly and dispose it
                        _ = GCHandle.Alloc(Interlocked.Exchange(ref srcPin, null));
                        _ = GCHandle.Alloc(Interlocked.Exchange(ref dstPin, null));

                        // we only want to dispose nativeDetour if we don't have a compileMethodHook
                        // if we *do* have a compileMethodHook, then it should stick around to ensure that
                        // the detour remains properly updated
                        if (compileMethodHook is null) {
                            var detour = Interlocked.Exchange(ref nativeDetour.Value, null);
                            if (detour is not null && disposing) {
                                detour.Dispose();
                            }
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
