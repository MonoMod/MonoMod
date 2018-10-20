using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;

namespace MonoMod.RuntimeDetour {
    public interface IDetourRuntimePlatform {
        IntPtr GetNativeStart(MethodBase method);
        DynamicMethod CreateCopy(MethodBase method);
        void Pin(MethodBase method);
    }

    public abstract class DetourRuntimeILPlatform : IDetourRuntimePlatform {
        protected abstract RuntimeMethodHandle GetMethodHandle(MethodBase method);

        // Prevent the GC from collecting those.
        protected HashSet<DynamicMethod> PinnedDynamicMethods = new HashSet<DynamicMethod>();

        public IntPtr GetNativeStart(MethodBase method) {
            RuntimeMethodHandle handle = GetMethodHandle(method);
            return handle.GetFunctionPointer();
        }

        public void Pin(MethodBase method) {
            RuntimeMethodHandle handle = GetMethodHandle(method);

            if (method is DynamicMethod) {
                DynamicMethod dm = (DynamicMethod) method;
                PinnedDynamicMethods.Add(dm);
            }

            RuntimeHelpers.PrepareMethod(handle);
        }

        public DynamicMethod CreateCopy(MethodBase method) {
            MethodBody body;
            try {
                body = method.GetMethodBody();
            } catch (InvalidOperationException) {
                body = null;
            } catch (NotSupportedException) {
                body = null;
            }
            if (body == null) {
                throw new InvalidOperationException("P/Invoke methods cannot be copied!");
            }

            ParameterInfo[] args = method.GetParameters();
            Type[] argTypes;
            if (!method.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = method.DeclaringType;
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            DynamicMethod dm = new DynamicMethod(
                $"orig_{method.Name}",
                // method.Attributes, method.CallingConvention, // DynamicMethod only supports public, static and standard
                (method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                method.DeclaringType,
                false
            );

            ILGenerator il = dm.GetILGenerator();

            // TODO: Move away from using Harmony's ILCopying code in MonoMod...
            using (Harmony.ILCopying.MethodCopier copier = new Harmony.ILCopying.MethodCopier(method, il)) {
                copier.Copy();
            }

            return dm.Pin();
        }
    }

    public sealed class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
        private readonly static FieldInfo f_DynamicMethod_m_method =
            typeof(DynamicMethod).GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly static FastReflectionDelegate _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.CreateFastDelegate();
        private readonly static FieldInfo f_RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly static MethodInfo m_RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly static FastReflectionDelegate _RuntimeHelpers__CompileMethod =
            m_RuntimeHelpers__CompileMethod?.CreateFastDelegate();
        private readonly static bool m_RuntimeHelpers__CompileMethod_TakesIntPtr =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IntPtr";
        private readonly static bool m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IRuntimeMethodInfo";

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                DynamicMethod dm = (DynamicMethod) method;
                if (m_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    // mscorlib 2.0.0.0
                    _RuntimeHelpers__CompileMethod(null, ((RuntimeMethodHandle) _DynamicMethod_GetMethodDescriptor(dm)).Value);

                } else if (m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
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

            return method.MethodHandle;
        }
    }

    public sealed class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private readonly static MethodInfo m_DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static FastReflectionDelegate dmd_DynamicMethod_CreateDynMethod =
            m_DynamicMethod_CreateDynMethod?.CreateFastDelegate();

        private readonly static FieldInfo f_DynamicMethod_mhandle =
            typeof(DynamicMethod).GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                dmd_DynamicMethod_CreateDynMethod?.Invoke(method);
                if (f_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) f_DynamicMethod_mhandle.GetValue(method);
            }

            return method.MethodHandle;
        }
    }

}
