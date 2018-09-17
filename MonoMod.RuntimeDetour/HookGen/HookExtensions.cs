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

        private static void _Insert(this ILProcessor il, Instruction at, Instruction instr) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            int index = instrs.IndexOf(at);
            if (index == -1)
                index = instrs.Count;
            instrs.Insert(index, instr);
        }

        #region Misc Helpers

        public static bool Is(this MemberReference member, string typeFullName, string name) {
            if (member == null)
                return false;
            return member.DeclaringType.FullName == typeFullName && member.Name == name;
        }

        public static bool Is(this MemberReference member, Type type, string name) {
            if (member == null)
                return false;
            return member.DeclaringType.FullName == type.FullName && member.Name == name;
        }

        public static bool Is(this MemberReference member, MemberInfo other) {
            if (member == null)
                return false;

            if (member.DeclaringType.FullName != other.DeclaringType.FullName)
                return false;
            if (member.Name != other.Name)
                return false;

            if (member is MethodReference mref) {
                if (!(other is MethodBase minf))
                    return false;

                Mono.Collections.Generic.Collection<ParameterDefinition> paramRefs = mref.Parameters;
                ParameterInfo[] paramInfos = minf.GetParameters();
                if (paramRefs.Count != paramInfos.Length)
                    return false;

                for (int i = 0; i < paramRefs.Count; i++) {
                    if (!paramRefs[i].ParameterType.Is(paramInfos[i].ParameterType))
                        return false;
                }
            }

            return true;
        }

        #endregion

        #region Misc IL Helpers

        public static bool GotoNext(this ILProcessor il, ref Instruction instr, Func<Mono.Collections.Generic.Collection<Instruction>, int, bool> predicate) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            try {
                for (int i = instrs.IndexOf(instr) + 1; i < instrs.Count; i++) {
                    if (predicate(instrs, i)) {
                        instr = instrs[i];
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public static bool GotoPrev(this ILProcessor il, ref Instruction instr, Func<Mono.Collections.Generic.Collection<Instruction>, int, bool> predicate) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            try {
                for (int i = instrs.IndexOf(instr) - 1; i > -1; i--) {
                    if (predicate(instrs, i)) {
                        instr = instrs[i];
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public static bool GotoNext(this ILProcessor il, ref Instruction instr, params Func<Instruction, bool>[] predicates) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            try {
                for (int i = instrs.IndexOf(instr) + 1; i + predicates.Length - 1 < instrs.Count; i++) {
                    bool match = true;
                    for (int j = 0; j < predicates.Length; j++) {
                        if (!(predicates[j]?.Invoke(instrs[i + j]) ?? true)) {
                            match = false;
                            break;
                        }
                    }
                    if (match) {
                        instr = instrs[i];
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public static bool GotoPrev(this ILProcessor il, ref Instruction instr, params Func<Instruction, bool>[] predicates) {
            Mono.Collections.Generic.Collection<Instruction> instrs = il.Body.Instructions;
            try {
                int i = instrs.IndexOf(instr) - 1;
                int overhang = i + predicates.Length - 1 - instrs.Count;
                if (overhang > 0)
                    i -= overhang;
                for (; i > -1; i--) {
                    bool match = true;
                    for (int j = 0; j < predicates.Length; j++) {
                        if (!(predicates[j]?.Invoke(instrs[i + j]) ?? true)) {
                            match = false;
                            break;
                        }
                    }
                    if (match) {
                        instr = instrs[i];
                        return true;
                    }
                }
            } catch {
                // Fail silently.
            }
            return false;
        }

        public static void ReplaceOperands(this ILProcessor il, object from, object to) {
            foreach (Instruction instr in il.Body.Instructions)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
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
        public static Instruction Create(this ILProcessor il, OpCode opcode, object operand) {
            Instruction instr = il.Create(opcode);
            instr.Operand = operand;
            return instr;
        }

        public static void Emit(this ILProcessor il, OpCode opcode, FieldInfo field)
            => il.Emit(opcode, il.Import(field));
        public static void Emit(this ILProcessor il, OpCode opcode, MethodBase method)
            => il.Emit(opcode, il.Import(method));
        public static void Emit(this ILProcessor il, OpCode opcode, Type type)
            => il.Emit(opcode, il.Import(type));
        public static void Emit(this ILProcessor il, OpCode opcode, object operand)
            => il.Append(il.Create(opcode, operand));

        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, ParameterDefinition parameter)
            => il._Insert(before, il.Create(opcode, parameter));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, VariableDefinition variable)
            => il._Insert(before, il.Create(opcode, variable));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, Instruction[] targets)
            => il._Insert(before, il.Create(opcode, targets));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, Instruction target)
            => il._Insert(before, il.Create(opcode, target));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, double value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, float value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, long value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, sbyte value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, byte value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, string value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, FieldReference field)
            => il._Insert(before, il.Create(opcode, field));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, CallSite site)
            => il._Insert(before, il.Create(opcode, site));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, TypeReference type)
            => il._Insert(before, il.Create(opcode, type));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode)
            => il._Insert(before, il.Create(opcode));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, int value)
            => il._Insert(before, il.Create(opcode, value));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, MethodReference method)
            => il._Insert(before, il.Create(opcode, method));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, FieldInfo field)
            => il._Insert(before, il.Create(opcode, field));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, MethodBase method)
            => il._Insert(before, il.Create(opcode, method));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, Type type)
            => il._Insert(before, il.Create(opcode, type));
        public static void Emit(this ILProcessor il, Instruction before, OpCode opcode, object operand)
            => il._Insert(before, il.Create(opcode, operand));

        #endregion

        #region Reference-oriented Emit Helpers

        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILProcessor il, T obj) {
            return il.EmitReference(null, obj);
        }
        /// <summary>
        /// Emit a reference to an arbitrary object. Note that the references "leak."
        /// </summary>
        public static int EmitReference<T>(this ILProcessor il, Instruction before, T obj) {
            MethodBody body = il.Body;

            Type t = typeof(T);
            int id = AddReference(obj);
            il.Emit(before, OpCodes.Ldc_I4, id);
            il.Emit(before, OpCodes.Call, _GetReference);
            if (t.IsValueType)
                il.Emit(before, OpCodes.Unbox_Any, t);
            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference and invocation.
        /// </summary>
        public static int EmitDelegateCall(this ILProcessor il, Action cb) {
            return il.EmitDelegateCall(null, cb);
        }
        /// <summary>
        /// Emit an inline delegate reference and invocation.
        /// </summary>
        public static int EmitDelegateCall(this ILProcessor il, Instruction before, Action cb) {
            int id = il.EmitDelegatePush(before, cb);
            il.EmitDelegateInvoke(before, id);
            return id;
        }

        /// <summary>
        /// Emit an inline delegate reference.
        /// </summary>
        public static int EmitDelegatePush<T>(this ILProcessor il, T cb) where T : Delegate {
            return il.EmitDelegatePush(null, cb);
        }
        /// <summary>
        /// Emit an inline delegate reference.
        /// </summary>
        public static int EmitDelegatePush<T>(this ILProcessor il, Instruction before, T cb) where T : Delegate
            => il.EmitReference(before, cb);

        /// <summary>
        /// Emit a delegate invocation.
        /// </summary>
        public static int EmitDelegateInvoke(this ILProcessor il, int id) {
            return il.EmitDelegateInvoke(null, id);
        }
        /// <summary>
        /// Emit a delegate invocation.
        /// </summary>
        public static int EmitDelegateInvoke(this ILProcessor il, Instruction before, int id) {
            il.Emit(before, OpCodes.Callvirt, References[id].GetType().GetMethod("Invoke"));
            return id;
        }

        #endregion

    }
}
