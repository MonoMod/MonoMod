using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour.Platforms {
    public sealed class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private static readonly object[] _NoArgs = new object[0];

        private static readonly MethodInfo _DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);

        // Let's just hope that those are present in Mono's implementation of .NET Standard 1.X
#if NETSTANDARD1_X
        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/Reflection/MethodInfo.cs#L613
        private static readonly MethodInfo _MethodBase_get_MethodHandle =
            typeof(MethodBase).GetMethod("get_MethodHandle", BindingFlags.Public | BindingFlags.Instance);

        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/RuntimeHandles.cs#L1045
        private static readonly MethodInfo _RuntimeMethodHandle_GetFunctionPointer =
            typeof(RuntimeMethodHandle).GetMethod("GetFunctionPointer", BindingFlags.Public | BindingFlags.Instance);

        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/Runtime/CompilerServices/RuntimeHelpers.cs#L102
        private static readonly MethodInfo _RuntimeHelpers_PrepareMethod =
            typeof(RuntimeHelpers).GetMethod("PrepareMethod", new Type[] { typeof(RuntimeMethodHandle) });

        protected override IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
            => (IntPtr) _RuntimeMethodHandle_GetFunctionPointer.Invoke(handle, _NoArgs);

        protected override void PrepareMethod(RuntimeMethodHandle handle)
            => _RuntimeHelpers_PrepareMethod.Invoke(null, new object[] { handle });
#endif

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                _DynamicMethod_CreateDynMethod?.Invoke(method, _NoArgs);
                if (_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) _DynamicMethod_mhandle.GetValue(method);
            }

#if NETSTANDARD1_X
            return (RuntimeMethodHandle) _MethodBase_get_MethodHandle.Invoke(method, _NoArgs);
#else
            return method.MethodHandle;
#endif
        }
    }

}
