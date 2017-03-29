using MonoMod.Helpers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.InlineRT {
    public delegate object DynamicMethodDelegate(object target, params object[] args);
    /// <summary>
    /// Stolen from http://theinstructionlimit.com/fast-net-reflection and FEZ. Thanks, Renaud!
    /// </summary>
    public static class ReflectionHelper {
        private static readonly Type[] manyObjects = new Type[2] { typeof(object), typeof(object[]) };
        private static readonly IDictionary<MethodInfo, DynamicMethodDelegate> methodCache = new FastDictionary<MethodInfo, DynamicMethodDelegate>();

        public static DynamicMethodDelegate CreateDelegate(this MethodBase method) {
            DynamicMethod dynam = new DynamicMethod(string.Empty, typeof(object), manyObjects, typeof(ReflectionHelper).Module, true);
            ILGenerator il = dynam.GetILGenerator();

            ParameterInfo[] args = method.GetParameters();

            Label argsOK = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Ldc_I4, args.Length);
            il.Emit(OpCodes.Beq, argsOK);

            il.Emit(OpCodes.Newobj, typeof(TargetParameterCountException).GetConstructor(Type.EmptyTypes));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(argsOK);

            if (!method.IsStatic && !method.IsConstructor) {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType.IsValueType) {
                    il.Emit(OpCodes.Unbox, method.DeclaringType);
                }
            }

            for (int i = 0; i < args.Length; i++) {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);

                if (args[i].ParameterType.IsValueType) {
                    il.Emit(OpCodes.Unbox_Any, args[i].ParameterType);
                }
            }

            if (method.IsConstructor) {
                il.Emit(OpCodes.Newobj, method as ConstructorInfo);
            } else if (method.IsFinal || !method.IsVirtual) {
                il.Emit(OpCodes.Call, method as MethodInfo);
            } else {
                il.Emit(OpCodes.Callvirt, method as MethodInfo);
            }

            Type returnType = method.IsConstructor ? method.DeclaringType : (method as MethodInfo).ReturnType;
            if (returnType != typeof(void)) {
                if (returnType.IsValueType) {
                    il.Emit(OpCodes.Box, returnType);
                }
            } else {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Ret);

            return (DynamicMethodDelegate) dynam.CreateDelegate(typeof(DynamicMethodDelegate));
        }

        public static DynamicMethodDelegate GetDelegate(this MethodInfo method) {
            DynamicMethodDelegate dmd;
            if (methodCache.TryGetValue(method, out dmd)) {
                return dmd;
            }

            dmd = CreateDelegate(method);
            methodCache.Add(method, dmd);

            return dmd;
        }

    }
}
