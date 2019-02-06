using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour.Platforms {
    public sealed class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private static readonly FastReflectionDelegate dmd_DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.CreateFastDelegate();

        private static readonly FieldInfo f_DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);

        // Let's just hope that those are present in Mono's implementation of .NET Standard 1.X
#if NETSTANDARD1_X
        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/Reflection/MethodInfo.cs#L613
        private static readonly FastReflectionDelegate _MethodBase_get_MethodHandle =
            typeof(MethodBase).GetMethod("get_MethodHandle", BindingFlags.Public | BindingFlags.Instance)
            ?.CreateFastDelegate();

        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/RuntimeHandles.cs#L1045
        private static readonly FastReflectionDelegate _RuntimeMethodHandle_GetFunctionPointer =
            typeof(RuntimeMethodHandle).GetMethod("GetFunctionPointer", BindingFlags.Public | BindingFlags.Instance)
            ?.CreateFastDelegate();

        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/Runtime/CompilerServices/RuntimeHelpers.cs#L102
        private static readonly FastReflectionDelegate _RuntimeHelpers_PrepareMethod =
            typeof(RuntimeHelpers).GetMethod("PrepareMethod", new Type[] { typeof(RuntimeMethodHandle) })
            ?.CreateFastDelegate();

        protected override IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
            => (IntPtr) _RuntimeMethodHandle_GetFunctionPointer(handle);

        protected override void PrepareMethod(RuntimeMethodHandle handle)
            => _RuntimeHelpers_PrepareMethod(null, handle);
#endif

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                dmd_DynamicMethod_CreateDynMethod?.Invoke(method);
                if (f_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) f_DynamicMethod_mhandle.GetValue(method);
            }

#if NETSTANDARD1_X
            return (RuntimeMethodHandle) _MethodBase_get_MethodHandle(method);
#else
            return method.MethodHandle;
#endif
        }
    }

}
