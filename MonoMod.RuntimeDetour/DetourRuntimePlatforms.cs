using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;

namespace MonoMod.RuntimeDetour {
    public interface IDetourRuntimePlatform {
        IntPtr GetNativeStart(MethodBase method);
        MethodInfo CreateCopy(MethodBase method);
        bool TryCreateCopy(MethodBase method, out MethodInfo dm);
        void Pin(MethodBase method);
        MethodBase GetDetourTarget(MethodBase from, MethodBase to);
    }

    public abstract class DetourRuntimeILPlatform : IDetourRuntimePlatform {
        protected abstract RuntimeMethodHandle GetMethodHandle(MethodBase method);

        // Prevent the GC from collecting those.
        protected HashSet<MethodBase> PinnedMethods = new HashSet<MethodBase>();

        private bool GlueThiscallStructRetPtr;

        public DetourRuntimeILPlatform() {
            // Perform a selftest if this runtime requires special handling for instance methods returning structs.
            // This is documented behavior for coreclr, but can implicitly affect all other runtimes (including mono!) as well.
            // Specifically, this should affect all __thiscalls

            MethodInfo selftest = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetStruct", BindingFlags.NonPublic | BindingFlags.Instance);
            Pin(selftest);
            MethodInfo selftestHook = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetStructHook", BindingFlags.NonPublic | BindingFlags.Static);
            Pin(selftestHook);
            NativeDetourData detour = DetourHelper.Native.Create(
                GetNativeStart(selftest),
                GetNativeStart(selftestHook)
            );
            DetourHelper.Native.MakeWritable(detour);
            DetourHelper.Native.Apply(detour);
            DetourHelper.Native.MakeExecutable(detour);
            DetourHelper.Native.Free(detour);
            // No need to undo the detour.

            _SelftestStruct s;
            // Make sure that the selftest isn't optimized away.
            try {
            } finally {
                s = _SelftestGetStruct(IntPtr.Zero, IntPtr.Zero);
                unsafe {
                    *&s = s;
                }
            }
        }

        #region Selftest: Struct

        // Struct must be 3, 5, 6, 7 or 9+ bytes big.
        private struct _SelftestStruct {
            private readonly byte A, B, C;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private _SelftestStruct _SelftestGetStruct(IntPtr x, IntPtr y) {
            throw new Exception("This method should've been detoured!");
        }

        private static unsafe void _SelftestGetStructHook(DetourRuntimeILPlatform self, IntPtr a, IntPtr b, IntPtr c) {
            // Normally, self = this, a = x, b = y
            // For coreclr x64 __thiscall, self = this, a = __ret, b = x, c = y

            // For the selftest, x must be equal to y.
            // If a != b, a is probably pointing to the return buffer.
            self.GlueThiscallStructRetPtr = a != b;
        }

        #endregion

        protected virtual IntPtr GetFunctionPointer(RuntimeMethodHandle handle)
#if NETSTANDARD1_X
            => throw new NotSupportedException();
#else
            => handle.GetFunctionPointer();
#endif

        protected virtual void PrepareMethod(RuntimeMethodHandle handle)
#if NETSTANDARD1_X
            => throw new NotSupportedException();
#else
            => RuntimeHelpers.PrepareMethod(handle);
#endif

        public IntPtr GetNativeStart(MethodBase method)
            => GetFunctionPointer(GetMethodHandle(method));

        public void Pin(MethodBase method) {
            PinnedMethods.Add(method);
            RuntimeMethodHandle handle = GetMethodHandle(method);
            PrepareMethod(handle);
        }

        public MethodInfo CreateCopy(MethodBase method) {
            if (!TryCreateCopy(method, out MethodInfo dm))
                throw new InvalidOperationException($"Uncopyable method: {method?.ToString() ?? "NULL"}");
            return dm;
        }
        public bool TryCreateCopy(MethodBase method, out MethodInfo dm) {
            if (method == null || (method.GetMethodImplementationFlags() & (MethodImplAttributes.OPTIL | MethodImplAttributes.Native | MethodImplAttributes.Runtime)) != 0) {
                dm = null;
                return false;
            }

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(method))
                dm = dmd.Generate();

            dm.Pin();
            return true;
        }

        public MethodBase GetDetourTarget(MethodBase from, MethodBase to) {
            MethodInfo fromInfo = from as MethodInfo;
            MethodInfo toInfo = to as MethodInfo;
            Type context = to.DeclaringType;

            DynamicMethod dm = null;

            if (GlueThiscallStructRetPtr &&
                fromInfo != null && !from.IsStatic &&
                toInfo != null && to.IsStatic &&
                fromInfo.ReturnType == toInfo.ReturnType &&
                fromInfo.ReturnType.GetTypeInfo().IsValueType) {
                int size = fromInfo.ReturnType.GetManagedSize();
                if (size == 3 || size == 5 || size == 6 || size == 7 || size >= 9) {
                    List<Type> argTypes = new List<Type>();
                    argTypes.Add(from.GetThisParamType()); // this
                    argTypes.Add(fromInfo.ReturnType.MakeByRefType()); // __ret - Refs are shiny pointers.
                    argTypes.AddRange(from.GetParameters().Select(p => p.ParameterType));
                    dm = new DynamicMethod(
                        $"Glue:ThiscallStructRetPtr<{from.GetFindableID(simple: true)},{to.GetFindableID(simple: true)}>",
                        typeof(void), argTypes.ToArray(),
                        true
                    );

                    ILGenerator il = dm.GetILGenerator();

                    // Load the return buffer address.
                    il.Emit(OpCodes.Ldarg, 1);

                    // Invoke the target method with all remaining arguments.
                    {
                        il.Emit(OpCodes.Ldarg, 0);
                        for (int i = 2; i < argTypes.Count; i++)
                            il.Emit(OpCodes.Ldarg, i);
                        il.Emit(OpCodes.Call, (MethodInfo) to);
                    }

                    // Store the returned object to the return buffer.
                    il.Emit(OpCodes.Stobj, fromInfo.ReturnType);
                    il.Emit(OpCodes.Ret);
                }
            }

            return dm ?? to;
        }
    }

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
