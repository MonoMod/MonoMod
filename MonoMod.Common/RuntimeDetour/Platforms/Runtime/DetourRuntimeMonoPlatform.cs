using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private static readonly object[] _NoArgs = new object[0];

        private static readonly MethodInfo _DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);

        // Prevent the GC from collecting those.
        protected Dictionary<MethodBase, MethodPin> PinnedMethods = new Dictionary<MethodBase, MethodPin>();

        public override IntPtr GetNativeStart(MethodBase method) {
            bool pinGot;
            MethodPin pin;
            lock (PinnedMethods)
                pinGot = PinnedMethods.TryGetValue(method, out pin);
            if (pinGot)
                return GetFunctionPointer(method, pin.Handle);
            return GetFunctionPointer(method, GetMethodHandle(method));
        }

        public override void Pin(MethodBase method) {
            lock (PinnedMethods) {
                if (PinnedMethods.TryGetValue(method, out MethodPin pin)) {
                    pin.Count++;
                    return;
                }

                pin = new MethodPin();
                pin.Count = 1;
                RuntimeMethodHandle handle = pin.Handle = GetMethodHandle(method);
                if (method.DeclaringType?.IsGenericType ?? false) {
                    PrepareMethod(method, handle, method.DeclaringType.GetGenericArguments().Select(type => type.TypeHandle).ToArray());
                } else {
                    PrepareMethod(method, handle);
                }
                DisableInlining(method, handle);
                PinnedMethods[method] = pin;
            }
        }

        public override void Unpin(MethodBase method) {
            lock (PinnedMethods) {
                if (!PinnedMethods.TryGetValue(method, out MethodPin pin))
                    return;

                if (pin.Count <= 1) {
                    PinnedMethods.Remove(method);
                    return;
                }
                pin.Count--;
            }
        }

        public override bool OnMethodCompiledWillBeCalled => false;
        public override event OnMethodCompiledEvent OnMethodCompiled;

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                _DynamicMethod_CreateDynMethod?.Invoke(method, _NoArgs);
                if (_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) _DynamicMethod_mhandle.GetValue(method);
            }

            return method.MethodHandle;
        }

        protected override unsafe void DisableInlining(MethodBase method, RuntimeMethodHandle handle) {
            // https://github.com/mono/mono/blob/34dee0ea4e969d6d5b37cb842fc3b9f73f2dc2ae/mono/metadata/class-internals.h#L64
            ushort* iflags = (ushort*) ((long) handle.Value + 2);
            *iflags |= (ushort) MethodImplOptions.NoInlining;
        }
    }

}
