using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour.Platforms {
    public sealed class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
        private static readonly object[] _NoArgs = new object[0];

        private static readonly FieldInfo _DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo _RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIntPtr =
            _RuntimeHelpers__CompileMethod != null &&
            _RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IntPtr";
        private static readonly bool _RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo =
            _RuntimeHelpers__CompileMethod != null &&
            _RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IRuntimeMethodInfo";

#if NETSTANDARD1_X
        private static readonly MethodInfo _MethodBase_get_MethodHandle =
            typeof(MethodBase).GetMethod("get_MethodHandle", BindingFlags.Public | BindingFlags.Instance);

        private static readonly MethodInfo _IRuntimeMethodInfo_get_Value =
            typeof(RuntimeMethodHandle).GetTypeInfo().Assembly
            .GetType("System.IRuntimeMethodInfo").GetMethod("get_Value", BindingFlags.Public | BindingFlags.Instance);
        private static readonly MethodInfo _RuntimeMethodHandle_GetFunctionPointer =
            typeof(RuntimeMethodHandle).GetMethod("GetFunctionPointer", BindingFlags.NonPublic | BindingFlags.Static);

        // .NET Core 1.0.0 should have GetFunctionPointer, but it only has got its internal static counterpart.
        protected override IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
            => (IntPtr) _RuntimeMethodHandle_GetFunctionPointer.Invoke(null, new object[] { _IRuntimeMethodInfo_get_Value.Invoke(_RuntimeMethodHandle_m_value.GetValue(handle), _NoArgs) });

        // .NET Core 1.0.0 should have PrepareMethod, but it has only got _CompileMethod.
        // Let's hope that it works as well.
        protected override void PrepareMethod(RuntimeMethodHandle handle)
            => _RuntimeHelpers__CompileMethod.Invoke(null, new object[] { _RuntimeMethodHandle_m_value.GetValue(handle) });
#endif

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            // Compile the method handle before getting our hands on the final method handle.
            if (method is DynamicMethod dm) {
#if !NETSTANDARD1_X
                if (_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    // mscorlib 2.0.0.0
                    _RuntimeHelpers__CompileMethod.Invoke(null, new object[] { ((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(dm, _NoArgs)).Value });

                } else
#endif
                if (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
                    // mscorlib 4.0.0.0
                    _RuntimeHelpers__CompileMethod.Invoke(null, new object[] { _RuntimeMethodHandle_m_value.GetValue(((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(dm, _NoArgs))) });

                } else {
                    // This should work just fine.
                    // It abuses the fact that CreateDelegate first compiles the DynamicMethod, before creating the delegate and failing.
                    // Only side effect: It introduces a possible deadlock in f.e. tModLoader, which adds a FirstChanceException handler.
                    try {
                        dm.CreateDelegate(typeof(MulticastDelegate));
                    } catch {
                    }
                }

                if (_DynamicMethod_m_method != null)
                    return (RuntimeMethodHandle) _DynamicMethod_m_method.GetValue(method);
                if (_DynamicMethod_GetMethodDescriptor != null)
                    return (RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(method, _NoArgs);
            }

#if NETSTANDARD1_X
            return (RuntimeMethodHandle) _MethodBase_get_MethodHandle.Invoke(method, _NoArgs);
#else
            return method.MethodHandle;
#endif
        }
    }
}
