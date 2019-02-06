using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour.Platforms {
    public sealed class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
        private static readonly FieldInfo f_DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FastReflectionDelegate _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.CreateFastDelegate();
        private static readonly FieldInfo f_RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo m_RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FastReflectionDelegate _RuntimeHelpers__CompileMethod =
            m_RuntimeHelpers__CompileMethod?.CreateFastDelegate();
        private static readonly bool m_RuntimeHelpers__CompileMethod_TakesIntPtr =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IntPtr";
        private static readonly bool m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IRuntimeMethodInfo";

#if NETSTANDARD1_X
        private static readonly FastReflectionDelegate _MethodBase_get_MethodHandle =
            typeof(MethodBase).GetMethod("get_MethodHandle", BindingFlags.Public | BindingFlags.Instance)
            ?.CreateFastDelegate();

        private static readonly FastReflectionDelegate _IRuntimeMethodInfo_get_Value =
            typeof(RuntimeMethodHandle).GetTypeInfo().Assembly
            .GetType("System.IRuntimeMethodInfo").GetMethod("get_Value", BindingFlags.Public | BindingFlags.Instance)
            ?.CreateFastDelegate();
        private static readonly FastReflectionDelegate _RuntimeMethodHandle_GetFunctionPointer =
            typeof(RuntimeMethodHandle).GetMethod("GetFunctionPointer", BindingFlags.NonPublic | BindingFlags.Static)
            ?.CreateFastDelegate();

        // .NET Core 1.0.0 should have GetFunctionPointer, but it only has got its internal static counterpart.
        protected override IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
            => (IntPtr) _RuntimeMethodHandle_GetFunctionPointer(null, _IRuntimeMethodInfo_get_Value(f_RuntimeMethodHandle_m_value.GetValue(handle)));

        // .NET Core 1.0.0 should have PrepareMethod, but it has only got _CompileMethod.
        // Let's hope that it works as well.
        protected override void PrepareMethod(RuntimeMethodHandle handle)
            => _RuntimeHelpers__CompileMethod(null, f_RuntimeMethodHandle_m_value.GetValue(handle));
#endif

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                DynamicMethod dm = (DynamicMethod) method;
#if !NETSTANDARD1_X
                if (m_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    // mscorlib 2.0.0.0
                    _RuntimeHelpers__CompileMethod(null, ((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor(dm)).Value);

                } else
#endif
                if (m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
                    // mscorlib 4.0.0.0
                    _RuntimeHelpers__CompileMethod(null, f_RuntimeMethodHandle_m_value.GetValue(((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor(dm))));

                } else {
                    // This should work just fine.
                    // It abuses the fact that CreateDelegate first compiles the DynamicMethod, before creating the delegate and failing.
                    // Only side effect: It introduces a possible deadlock in f.e. tModLoader, which adds a FirstChanceException handler.
                    try {
                        dm.CreateDelegate(typeof(MulticastDelegate));
                    } catch {
                    }
                }

                if (f_DynamicMethod_m_method != null)
                    return (RuntimeMethodHandle) f_DynamicMethod_m_method.GetValue(method);
                if (_DynamicMethod_GetMethodDescriptor != null)
                    return (RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor(method);
            }

#if NETSTANDARD1_X
            return (RuntimeMethodHandle) _MethodBase_get_MethodHandle(method);
#else
            return method.MethodHandle;
#endif
        }
    }
}
