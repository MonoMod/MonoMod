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
    class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
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

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            // Compile the method handle before getting our hands on the final method handle.
            if (method is DynamicMethod dm) {
                if (_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    // mscorlib 2.0.0.0
                    _RuntimeHelpers__CompileMethod.Invoke(null, new object[] { ((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor.Invoke(dm, _NoArgs)).Value });

                } else if (_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
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

            return method.MethodHandle;
        }

        protected override void DisableInlining(RuntimeMethodHandle handle) {
            // This is not needed for .NET Framework - see DisableInliningTest.
        }
    }
}
