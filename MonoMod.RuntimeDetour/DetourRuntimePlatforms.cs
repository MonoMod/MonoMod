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
            MethodBody body = method.GetMethodBody();
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
            List<Label> endLabels = new List<Label>();
            List<Harmony.ILCopying.ExceptionBlock> endBlocks = new List<Harmony.ILCopying.ExceptionBlock>();
            Harmony.ILCopying.MethodCopier copier = new Harmony.ILCopying.MethodCopier(method, il);
            copier.Finalize(endLabels, endBlocks);
            foreach (Label label in endLabels)
                il.MarkLabel(label);
            foreach (Harmony.ILCopying.ExceptionBlock block in endBlocks)
                Harmony.ILCopying.Emitter.MarkBlockAfter(il, block);

            return dm.Pin();
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
            typeof(RuntimeMethodHandle).GetMethod("GetMethodInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly static DynamicMethodDelegate dmd_RuntimeMethodHandle_GetMethodInfo =
            m_RuntimeMethodHandle_GetMethodInfo?.CreateDelegate();

        protected override RuntimeMethodHandle GetMethodHandle(MethodBase method) {
            if (method is DynamicMethod) {
                // Compile the method handle before getting our hands on the final method handle.
                DynamicMethod dm = (DynamicMethod) method;
                // This likes to die.
                /*
                if (m_RuntimeHelpers__CompileMethod_TakesIntPtr) {
                    dmd_RuntimeHelpers__CompileMethod(null, ((RuntimeMethodHandle) dmd_DynamicMethod_GetMethodDescriptor(dm)).Value);
                } else if (m_RuntimeHelpers__CompileMethod_TakesIRuntimeMethodInfo) {
                    dmd_RuntimeHelpers__CompileMethod(null, dmd_RuntimeMethodHandle_GetMethodInfo((RuntimeMethodHandle) dmd_DynamicMethod_GetMethodDescriptor(dm)));
                }
                */
                // This should work just fine.
                // It abuses the fact that CreateDelegate first compiles the DynamicMethod, before creating the delegate and failing.
                try {
                    dm.CreateDelegate(typeof(MulticastDelegate));
                } catch {
                }

                if (f_DynamicMethod_m_method != null)
                    return (RuntimeMethodHandle) f_DynamicMethod_m_method.GetValue(method);
                if (dmd_DynamicMethod_GetMethodDescriptor != null)
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
