using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using System.ComponentModel;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace MonoMod.RuntimeDetour.HookGen {
    public static class HookExtensions {

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

        private readonly static MethodInfo _GetReference = typeof(HookExtensions).GetMethod("GetReference");

        private static int _WrapIndex(this MethodBody body, int index) {
            if (index < 0)
                return body.Instructions.Count + index;
            if (index > body.Instructions.Count)
                return body.Instructions.Count;
            return index;
        }

        public static Instruction Create(this ILProcessor il, OpCode opcode, FieldInfo field)
            => il.Create(opcode, il.Body.Method.Module.ImportReference(field));
        public static Instruction Create(this ILProcessor il, OpCode opcode, MethodBase method)
            => il.Create(opcode, il.Body.Method.Module.ImportReference(method));
        public static Instruction Create(this ILProcessor il, OpCode opcode, Type type)
            => il.Create(opcode, il.Body.Method.Module.ImportReference(type));

        public static void Emit(this ILProcessor il, OpCode opcode, FieldInfo field)
            => il.Emit(opcode, il.Body.Method.Module.ImportReference(field));
        public static void Emit(this ILProcessor il, OpCode opcode, MethodBase method)
            => il.Emit(opcode, il.Body.Method.Module.ImportReference(method));
        public static void Emit(this ILProcessor il, OpCode opcode, Type type)
            => il.Emit(opcode, il.Body.Method.Module.ImportReference(type));

        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILProcessor il, T obj) {
            int index = int.MaxValue;
            return il.EmitReference(obj, ref index);
        }
        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILProcessor il, T obj, ref int index) {
            MethodBody body = il.Body;
            Mono.Collections.Generic.Collection<Instruction> instrs = body.Instructions;
            index = body._WrapIndex(index);

            Type t = typeof(T);
            int id = AddReference(obj);
            instrs.Insert(index, il.Create(OpCodes.Ldc_I4, id));
            index++;
            body.Instructions.Insert(index, il.Create(OpCodes.Call, _GetReference));
            index++;
            if (t.IsValueType) {
                instrs.Insert(index, il.Create(OpCodes.Unbox_Any, t));
                index++;
            }
            return id;
        }

        /// <summary>
        /// Emit an inline delegate call.
        /// </summary>
        public static int EmitDelegateCall<T>(this ILProcessor il, T cb, int callOffset = 0) where T : Delegate {
            int index = int.MaxValue;
            return il.EmitDelegateCall(cb, ref index, callOffset);
        }
        /// <summary>
        /// Emit an inline delegate call.
        /// </summary>
        public static int EmitDelegateCall<T>(this ILProcessor il, T cb, ref int index, int callOffset = 0) where T : Delegate {
            MethodBody body = il.Body;
            Mono.Collections.Generic.Collection<Instruction> instrs = body.Instructions;
            index = body._WrapIndex(index);

            int id = il.EmitReference(cb, ref index);

            index += callOffset;
            instrs.Insert(index, il.Create(OpCodes.Callvirt, typeof(T).GetMethod("Invoke")));
            index++;

            return id;
        }

    }
}
