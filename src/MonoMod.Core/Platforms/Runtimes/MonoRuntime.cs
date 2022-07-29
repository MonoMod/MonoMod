using MonoMod.Backports;
using MonoMod.Core.Utils;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class MonoRuntime : IRuntime {
        public RuntimeKind Target => RuntimeKind.Mono;

        public RuntimeFeature Features =>
            RuntimeFeature.DisableInlining |
            RuntimeFeature.RequiresMethodPinning |
            RuntimeFeature.RequiresMethodIdentification |
            RuntimeFeature.GenericSharing;

        public Abi Abi { get; }

        private readonly ISystem system;

        public MonoRuntime(ISystem system) {
            this.system = system;

            // see https://github.com/dotnet/runtime/blob/v6.0.5/src/mono/mono/mini/mini-amd64.c line 472, 847, 1735
            if (system.DefaultAbi is { } abi) {
                // notably, in Mono, the generic context pointer is not an argument in the normal calling convention, but an argument elsewhere (r11 on x64)
                Abi = abi;
            } else {
                throw new InvalidOperationException("Cannot use Mono system, because the underlying system doesn't provide a default ABI!");
            }
        }

        public event OnMethodCompiledCallback? OnMethodCompiled;

        public unsafe void DisableInlining(MethodBase method) {
            var handle = GetMethodHandle(method);
            // https://github.com/mono/mono/blob/34dee0ea4e969d6d5b37cb842fc3b9f73f2dc2ae/mono/metadata/class-internals.h#L64
            var iflags = (ushort*) ((long) handle.Value + 2);
            *iflags |= (ushort) MethodImplOptionsEx.NoInlining;
        }

        private static readonly MethodInfo _DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo _DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance)!;

        public RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            // Compile the method handle before getting our hands on the final method handle.
            // Note that Mono can return RuntimeMethodInfo instead of DynamicMethod in some places, thus bypassing this.
            // Let's assume that the method was already compiled ahead of this method call if that is the case.
            if (method is DynamicMethod) {
                _DynamicMethod_CreateDynMethod?.Invoke(method, ArrayEx.Empty<object?>());
                if (_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) _DynamicMethod_mhandle.GetValue(method)!;
            }

            return method.MethodHandle;
        }

        private class PrivateMethodPin {
            private readonly MonoRuntime runtime;
            public PrivateMethodPin(MonoRuntime runtime)
                => this.runtime = runtime;

            public MethodPinInfo Pin;

            public void UnpinOnce() => runtime.UnpinOnce(this);
        }

        private class PinHandle : IDisposable {
            private readonly PrivateMethodPin pin;
            public PinHandle(PrivateMethodPin pin) {
                this.pin = pin;
            }

            private bool disposedValue;

            protected virtual void Dispose(bool disposing) {
                if (!disposedValue) {
                    if (disposing) {
                        // dispose managed state (managed objects)
                    }

                    pin.UnpinOnce();
                    disposedValue = true;
                }
            }

            ~PinHandle()
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

        private struct MethodPinInfo {
            public int Count;
            public MethodBase Method;
            public RuntimeMethodHandle Handle;

            public override string ToString() {
                return $"(MethodPinInfo: {Count}, {Method}, 0x{(long) Handle.Value:X})";
            }
        }

        private readonly ConcurrentDictionary<MethodBase, PrivateMethodPin> pinnedMethods = new();
        private readonly ConcurrentDictionary<RuntimeMethodHandle, PrivateMethodPin> pinnedHandles = new();

        public IDisposable? PinMethodIfNeeded(MethodBase method) {
            method = GetIdentifiable(method);

            var pin = pinnedMethods.GetOrAdd(method, m => {
                var pin = new PrivateMethodPin(this);

                pin.Pin.Method = m;
                RuntimeMethodHandle handle = pin.Pin.Handle = GetMethodHandle(m);
                pinnedHandles[handle] = pin;

                DisableInlining(method);
                if (method.DeclaringType?.IsGenericType ?? false) {
                    // TODO: PrepareMethod
                    //PrepareMethod(method, handle, method.DeclaringType.GetGenericArguments().Select(type => type.TypeHandle).ToArray());
                } else {
                    //PrepareMethod(method, handle);
                }

                return pin;
            });
            Interlocked.Increment(ref pin.Pin.Count);

            return new PinHandle(pin);
        }

        private void UnpinOnce(PrivateMethodPin pin) {
            if (Interlocked.Decrement(ref pin.Pin.Count) <= 0) {
                pinnedMethods.TryRemove(pin.Pin.Method, out _);
                pinnedHandles.TryRemove(pin.Pin.Handle, out _);
            }
        }

        public MethodBase GetIdentifiable(MethodBase method) {
            return pinnedHandles.TryGetValue(GetMethodHandle(method), out var pin) ? pin.Pin.Method : method;
        }

        public IntPtr GetMethodEntryPoint(MethodBase method) {
            if (pinnedMethods.TryGetValue(method, out var pmp)) {
                return pmp.Pin.Handle.GetFunctionPointer();
            }
            var handle = GetMethodHandle(method);
            return handle.GetFunctionPointer();
        }
    }
}
