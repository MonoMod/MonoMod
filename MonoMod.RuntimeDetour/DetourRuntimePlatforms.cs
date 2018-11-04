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
        bool TryCreateCopy(MethodBase method, out DynamicMethod dm);
        void Pin(MethodBase method);
    }

    public abstract class DetourRuntimeILPlatform : IDetourRuntimePlatform {
        protected abstract RuntimeMethodHandle GetMethodHandle(MethodBase method);

        // Prevent the GC from collecting those.
        protected HashSet<DynamicMethod> PinnedDynamicMethods = new HashSet<DynamicMethod>();

        // MethodInfo.get_MethodHandle, RuntimeMethodHandle.GetFunctionPointer and RuntimeHelpers.PrepareMethod are present in coreclr's mscorlib 1.0.0
        // Weirdly enough, they're not exposed in .NET Standard until 2.0
#if NETSTANDARD1_X
        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/Reflection/MethodInfo.cs#L613
        private static readonly FastReflectionDelegate _MethodInfo_get_MethodHandle =
            typeof(MethodInfo).GetTypeInfo().GetProperty("MethodHandle", BindingFlags.Public | BindingFlags.Instance).GetGetMethod()
            ?.CreateFastDelegate();

        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/RuntimeHandles.cs#L1045
        private static readonly FastReflectionDelegate _RuntimeMethodHandle_GetFunctionPointer =
            typeof(RuntimeMethodHandle).GetTypeInfo().GetMethod("GetFunctionPointer", BindingFlags.Public | BindingFlags.Instance)
            ?.CreateFastDelegate();

        // https://github.com/dotnet/coreclr/blob/release/1.0.0/src/mscorlib/src/System/Runtime/CompilerServices/RuntimeHelpers.cs#L102
        private static readonly FastReflectionDelegate _RuntimeHelpers_PrepareMethod =
            typeof(RuntimeHelpers).GetTypeInfo().GetMethod("PrepareMethod", BindingFlags.Public | BindingFlags.Static)
            ?.CreateFastDelegate();
#endif

        protected RuntimeMethodHandle GetMethodHandleDirect(MethodBase method)
#if NETSTANDARD1_X
            => (RuntimeMethodHandle) _MethodInfo_get_MethodHandle(method);
#else
            => method.MethodHandle;
#endif

        protected IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
#if NETSTANDARD1_X
            => (IntPtr) _RuntimeMethodHandle_GetFunctionPointer(handle);
#else
            => handle.GetFunctionPointer();
#endif

        protected void PrepareMethod(RuntimeMethodHandle handle)
#if NETSTANDARD1_X
            => _RuntimeHelpers_PrepareMethod(handle);
#else
            => RuntimeHelpers.PrepareMethod(handle);
#endif

        public IntPtr GetNativeStart(MethodBase method)
            => GetFunctionPointer(GetMethodHandle(method));

        public void Pin(MethodBase method) {
            RuntimeMethodHandle handle = GetMethodHandle(method);

            if (method is DynamicMethod) {
                DynamicMethod dm = (DynamicMethod) method;
                PinnedDynamicMethods.Add(dm);
            }

            PrepareMethod(handle);
        }

        public DynamicMethod CreateCopy(MethodBase method) {
            if (!TryCreateCopy(method, out DynamicMethod dm))
                throw new InvalidOperationException($"Uncopyable method: {method.ToString()}");
            return dm;
        }
        public bool TryCreateCopy(MethodBase method, out DynamicMethod dm) {
            if ((method.GetMethodImplementationFlags() & (MethodImplAttributes.OPTIL | MethodImplAttributes.Native | MethodImplAttributes.Runtime)) != 0) {
                dm = null;
                return false;
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

#if NETSTANDARD1_X
            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(method))
                dm = dmd.Generate();
#else
            dm = new DynamicMethod(
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
#endif

            dm.Pin();
            return true;
        }
    }

    public sealed class DetourRuntimeNETPlatform : DetourRuntimeILPlatform {
        private static readonly FieldInfo f_DynamicMethod_m_method =
            typeof(DynamicMethod).GetTypeInfo().GetField("m_method", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FastReflectionDelegate _DynamicMethod_GetMethodDescriptor =
            typeof(DynamicMethod).GetTypeInfo().GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.CreateFastDelegate();
        private static readonly FieldInfo f_RuntimeMethodHandle_m_value =
            typeof(RuntimeMethodHandle).GetTypeInfo().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo m_RuntimeHelpers__CompileMethod =
            typeof(RuntimeHelpers).GetTypeInfo().GetMethod("_CompileMethod", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FastReflectionDelegate _RuntimeHelpers__CompileMethod =
            m_RuntimeHelpers__CompileMethod?.CreateFastDelegate();
        private static readonly bool m_RuntimeHelpers__CompileMethod_TakesIntPtr =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IntPtr";
        private static readonly bool m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo =
            m_RuntimeHelpers__CompileMethod != null &&
            m_RuntimeHelpers__CompileMethod.GetParameters()[0].ParameterType.FullName == "System.IRuntimeMethodInfo";

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

            return GetMethodHandleDirect(method);
        }
    }

    public sealed class DetourRuntimeMonoPlatform : DetourRuntimeILPlatform {
        private static readonly MethodInfo m_DynamicMethod_CreateDynMethod =
            typeof(DynamicMethod).GetTypeInfo().GetMethod("CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FastReflectionDelegate dmd_DynamicMethod_CreateDynMethod =
            m_DynamicMethod_CreateDynMethod?.CreateFastDelegate();

        private static readonly FieldInfo f_DynamicMethod_mhandle =
            typeof(DynamicMethod).GetTypeInfo().GetField("mhandle", BindingFlags.NonPublic | BindingFlags.Instance);

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                dmd_DynamicMethod_CreateDynMethod?.Invoke(method);
                if (f_DynamicMethod_mhandle != null)
                    return (RuntimeMethodHandle) f_DynamicMethod_mhandle.GetValue(method);
            }

            return GetMethodHandleDirect(method);
        }
    }

}
