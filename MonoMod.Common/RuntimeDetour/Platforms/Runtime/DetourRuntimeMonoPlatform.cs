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
    sealed class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private static readonly object[] _NoArgs = new object[0];

        private static readonly MethodInfo _DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                _DynamicMethod_CreateDynMethod?.Invoke(method, _NoArgs);
                if (_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) _DynamicMethod_mhandle.GetValue(method);
            }

            return method.MethodHandle;
        }
    }

}
