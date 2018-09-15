using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using System.ComponentModel;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;

namespace MonoMod.RuntimeDetour.HookGen {
    public static class HookExtensions {

        // This delegate will be cloned into the wrapper inside of the generated assembly.
        public delegate void ILManipulator(MethodBody body, ILProcessor il);

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

        private static int _Step(this ILProcessor il, ref int index) {
            int tmp = il.ClampWrapIndex(index);
            index = tmp + 1;
            return tmp;
        }

        #region Misc Helpers

        public static int ClampWrapIndex(this ILProcessor il, int index) {
            int count = il.Body.Instructions.Count;
            if (index < 0)
                return count + index;
            if (index > count)
                return count;
            return index;
        }

        public static bool GotoNext(this ILProcessor il, ref int index, Func<Mono.Collections.Generic.Collection<Instruction>, int, bool> predicate) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            try {
                for (int i = il.ClampWrapIndex(index + 1); i < instrs.Count; i++) {
                    if (predicate(instrs, i)) {
                        index = i;
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public static bool GotoPrev(this ILProcessor il, ref int index, Func<Mono.Collections.Generic.Collection<Instruction>, int, bool> predicate) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            for (int i = il.ClampWrapIndex(index - 1); i > -1; i--) {
                if (predicate(instrs, i)) {
                    index = i;
                    return true;
                }
            }
            return false;
        }

        public static void UpdateBranches(this ILProcessor il, int indexFrom, int indexTo) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            Instruction instrFrom = instrs[indexFrom];
            Instruction instrTo = instrs[indexTo];
            foreach (Instruction instr in instrs)
                if (instr.Operand == instrFrom)
                    instr.Operand = instrTo;
        }

        #endregion

        #region Base Create / Emit Helpers

        public static FieldReference Import(this ILProcessor il, FieldInfo field)
            => il.Body.Method.Module.ImportReference(field);
        public static MethodReference Import(this ILProcessor il, MethodBase method)
            => il.Body.Method.Module.ImportReference(method);
        public static TypeReference Import(this ILProcessor il, Type type)
            => il.Body.Method.Module.ImportReference(type);

        public static Instruction Create(this ILProcessor il, OpCode opcode, FieldInfo field)
            => il.Create(opcode, il.Import(field));
        public static Instruction Create(this ILProcessor il, OpCode opcode, MethodBase method)
            => il.Create(opcode, il.Import(method));
        public static Instruction Create(this ILProcessor il, OpCode opcode, Type type)
            => il.Create(opcode, il.Import(type));

        public static void Emit(this ILProcessor il, OpCode opcode, FieldInfo field)
            => il.Emit(opcode, il.Import(field));
        public static void Emit(this ILProcessor il, OpCode opcode, MethodBase method)
            => il.Emit(opcode, il.Import(method));
        public static void Emit(this ILProcessor il, OpCode opcode, Type type)
            => il.Emit(opcode, il.Import(type));

        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, ParameterDefinition parameter)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, parameter));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, VariableDefinition variable)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, variable));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, Instruction[] targets)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, targets));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, Instruction target)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, target));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, double value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, float value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, long value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, sbyte value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, byte value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, string value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, FieldReference field)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, field));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, CallSite site)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, site));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, TypeReference type)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, type));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, int value)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, value));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, MethodReference method)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, method));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, FieldInfo field)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, field));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, MethodBase method)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, method));
        public static void Emit(this ILProcessor il, ref int index, OpCode opcode, Type type)
            => il.Body.Instructions.Insert(il._Step(ref index), il.Create(opcode, type));

        #endregion

        #region Reference-based Emit Helpers

        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILProcessor il, T obj) {
            int index = int.MaxValue;
            return il.EmitReference(ref index, obj);
        }
        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILProcessor il, ref int index, T obj) {
            MethodBody body = il.Body;
            index = il.ClampWrapIndex(index);

            Type t = typeof(T);
            int id = AddReference(obj);
            il.Emit(ref index, OpCodes.Ldc_I4, id);
            il.Emit(ref index, OpCodes.Call, _GetReference);
            if (t.IsValueType)
                il.Emit(ref index, OpCodes.Unbox_Any, t);
            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference and invocation.
        /// </summary>
        public static int EmitDelegateCall(this ILProcessor il, Action cb) {
            int index = int.MaxValue;
            return il.EmitDelegateCall(ref index, cb);
        }
        /// <summary>
        /// Emit an inline delegate reference and invocation.
        /// </summary>
        public static int EmitDelegateCall(this ILProcessor il, ref int index, Action cb) {
            int id = il.EmitDelegatePush(ref index, cb);
            il.EmitDelegateInvoke(ref index, id);
            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference.
        /// </summary>
        public static int EmitDelegatePush<T>(this ILProcessor il, T cb) where T : Delegate {
            int index = int.MaxValue;
            return il.EmitDelegatePush(ref index, cb);
        }
        /// <summary>
        /// Emit an inline delegate reference.
        /// </summary>
        public static int EmitDelegatePush<T>(this ILProcessor il, ref int index, T cb) where T : Delegate
            => il.EmitReference(ref index, cb);

        /// <summary>
        /// Emit a delegate invocation.
        /// </summary>
        public static int EmitDelegateInvoke(this ILProcessor il, int id) {
            int index = int.MaxValue;
            return il.EmitDelegateInvoke(ref index, id);
        }
        /// <summary>
        /// Emit a delegate invocation.
        /// </summary>
        public static int EmitDelegateInvoke(this ILProcessor il, ref int index, int id) {
            index = il.ClampWrapIndex(index);

            il.Emit(ref index, OpCodes.Callvirt, References[id].GetType().GetMethod("Invoke"));

            return id;
        }

        #endregion

    }
}
