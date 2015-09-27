using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.JIT {
    public delegate object DynamicMethodDelegate(object target, params object[] args);
    /// <summary>
    /// Stolen from http://theinstructionlimit.com/fast-net-reflection and FEZ. Thanks, Renaud!
    /// </summary>
    public static class ReflectionHelper {
        private static readonly Type[] manyObjects = new Type[2] {typeof(object), typeof (object[])};
        private static readonly Dictionary<MethodInfo, DynamicMethodDelegate> methodCache = new Dictionary<MethodInfo, DynamicMethodDelegate>();

        public static DynamicMethodDelegate CreateDelegate(this MethodBase method) {
            var dynam = new DynamicMethod(string.Empty, typeof(object), manyObjects, typeof(ReflectionHelper).Module, true);
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

            il.PushInstance(method.DeclaringType);

            for (int i = 0; i < args.Length; i++) {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);

                il.UnboxIfNeeded(args[i].ParameterType);
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
                il.BoxIfNeeded(returnType);
            } else {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Ret);

            return (MethodHandler) dynam.CreateDelegate(typeof(MethodHandler));
        }

        public static DynamicMethodDelegate GetDelegate(this MethodInfo method) {
            DynamicMethodDelegate dmd;
            if (!ReflectionHelper.methodCache.TryGetValue(method, out dmd)) {
                return dmd;
            }

            dmd = ReflectionHelper.CreateDelegate(method);
            ReflectionHelper.methodCache.Add(method, dmd);

            return dmd;
        }

    }

    public struct HandlePair<T, U> : IEquatable<HandlePair<T, U>> {
        private readonly T t;
        private readonly U u;
        private readonly int hash;

        public HandlePair(T t, U u) {
            this.t = t;
            this.u = u;
            hash = 27232 + t.GetHashCode();
        }

        public override int GetHashCode() {
            return hash;
        }

        public override bool Equals(object obj) {
            if (obj == null || !(obj is HandlePair<T, U>)) {
                return false;
            }
            return Equals((HandlePair<T, U>) obj);
        }

        public bool Equals(HandlePair<T, U> obj) {
            return obj.t.Equals(t) && obj.u.Equals(u);
        }
    }
}

