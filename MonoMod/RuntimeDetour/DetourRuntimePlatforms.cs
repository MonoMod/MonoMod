using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;

namespace MonoMod.RuntimeDetour {
    public interface IDetourRuntimePlatform {
        IntPtr GetJITStart(MethodBase method);
    }

    public abstract class DetourRuntimeILPlatform : IDetourRuntimePlatform {
        protected abstract RuntimeMethodHandle GetMethodHandle(MethodBase method);

        public IntPtr GetJITStart(MethodBase method) {
            RuntimeMethodHandle handle = GetMethodHandle(method);
            // "Pin" the method.
            RuntimeHelpers.PrepareMethod(handle);
            return handle.GetFunctionPointer();
        }
    }

    public sealed class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
        private readonly static FieldInfo f_DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly static MethodInfo m_DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_DynamicMethod_GetMethodDescriptor =
            m_DynamicMethod_GetMethodDescriptor?.CreateDelegate();

        private readonly static MethodInfo m_RuntimeHelpers__CompileMethod =
            // .NET
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly static DynamicMethodDelegate dmd_RuntimeHelpers__CompileMethod =
            m_RuntimeHelpers__CompileMethod?.CreateDelegate();
        private readonly static bool m_RuntimeHelpers__CompileMethod_TakesIntPtr =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IntPtr";
        private readonly static bool m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IRuntimeMethodInfo";

        private readonly static MethodInfo m_RuntimeMethodHandle_GetMethodInfo =
            // .NET
            typeof(RuntimeMethodHandle).GetMethod("GetMethodInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_RuntimeMethodHandle_GetMethodInfo =
            m_RuntimeMethodHandle_GetMethodInfo?.CreateDelegate();

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                DynamicMethod dm = (DynamicMethod) method;
                if (m_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    dmd_RuntimeHelpers__CompileMethod(null, ((RuntimeMethodHandle) dmd_DynamicMethod_GetMethodDescriptor(dm)).Value);
                } else if (m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
                    // This likes to die.
                    // dmd_RuntimeHelpers__CompileMethod(null, dmd_RuntimeMethodHandle_GetMethodInfo(handle));
                    // This should work just fine.
                    try {
                        dm.CreateDelegate(typeof(MulticastDelegate));
                    } catch {
                    }
                }

                if (f_DynamicMethod_m_method != null)
                    return (RuntimeMethodHandle) f_DynamicMethod_m_method.GetValue(method);
                else if (dmd_DynamicMethod_GetMethodDescriptor != null)
                    return (RuntimeMethodHandle) dmd_DynamicMethod_GetMethodDescriptor(method);
            }

            return method.MethodHandle;
        }
    }

    public sealed class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private readonly static MethodInfo m_DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_DynamicMethod_CreateDynMethod =
            m_DynamicMethod_CreateDynMethod?.CreateDelegate();

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                dmd_DynamicMethod_CreateDynMethod?.Invoke(method);
                // Mono doesn't hide the DynamicMethod handle.
            }

            return method.MethodHandle;
        }
    }

}
