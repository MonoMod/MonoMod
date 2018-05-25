using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;

namespace MonoMod.Utils {
    public static class DynamicMethodHelper {

        // Used in EmitReference.
        private static List<object> References = new List<object>();
        public static object GetReference(int id) => References[id];
        public static void SetReference(int id, object obj) => References[id] = obj;
        private static int AddReference(object obj) {
            lock (References) {
                References.Add(obj);
                return References.Count - 1;
            }
        }
        public static void FreeReference(int id) => References[id] = null;

        private readonly static MethodInfo _GetMethodFromHandle = typeof(MethodBase).GetMethod("GetMethodFromHandle", new Type[] { typeof(RuntimeMethodHandle) });
        private readonly static MethodInfo _GetReference = typeof(DynamicMethodHelper).GetMethod("GetReference");

        /// <summary>
        /// Fill the DynamicMethod with a stub.
        /// </summary>
        public static DynamicMethod Stub(this DynamicMethod dm) {
            ILGenerator il = dm.GetILGenerator();
            for (int i = 0; i < 10; i++) {
                // Prevent old Unity mono from inlining the DynamicMethod.
                il.Emit(OpCodes.Nop);
            }
            if (dm.ReturnType != typeof(void)) {
                il.DeclareLocal(dm.ReturnType);
                il.Emit(OpCodes.Ldloca_S, (sbyte) 0);
                il.Emit(OpCodes.Initobj, dm.ReturnType);
                il.Emit(OpCodes.Ldloc_0);
            }
            il.Emit(OpCodes.Ret);
            return dm;
        }

        /// <summary>
        /// Emit a ldtoken + MethodBase.GetMethodFromHandle. This would be methodof(...) in C#, if it would exist.
        /// </summary>
        public static void EmitMethodOf(this ILGenerator il, MethodBase method) {
            if (method is MethodInfo)
                il.Emit(OpCodes.Ldtoken, (MethodInfo) method);
            else if (method is ConstructorInfo)
                il.Emit(OpCodes.Ldtoken, (ConstructorInfo) method);
            else
                throw new NotSupportedException($"Method type {method.GetType().FullName} not supported.");

            il.Emit(OpCodes.Call, _GetMethodFromHandle);
        }

        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILGenerator il, T obj) {
            Type t = typeof(T);
            int id = AddReference(obj);
            il.Emit(OpCodes.Ldc_I4, id);
            il.Emit(OpCodes.Call, _GetReference);
            if (t.IsValueType)
                il.Emit(OpCodes.Unbox_Any, t);
            return id;
        }

    }
}
