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
    // This delegate will be used when the HookGen-erated assembly "exposes" RuntimeDetour references.
    public delegate void ILManipulator(HookIL il);
    public static partial class HookExtensions {

        // This delegate will be cloned into the wrapper inside of the generated assembly.
        public delegate void ILManipulatorMini(MethodBody body, ILProcessor il);

        #region Misc Helpers

        public static bool Is(this MemberReference member, string fullName) {
            if (member == null)
                return false;
            return member.FullName == fullName;
        }

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
        public static Instruction Create(this ILProcessor il, OpCode opcode, MethodBase method) {
            if (method is System.Reflection.Emit.DynamicMethod)
                return il.Create(opcode, (object) method);
            return il.Create(opcode, il.Import(method));
        }
        public static Instruction Create(this ILProcessor il, OpCode opcode, Type type)
            => il.Create(opcode, il.Import(type));
        public static Instruction Create(this ILProcessor il, OpCode opcode, object operand) {
            Instruction instr = il.Create(OpCodes.Nop);
            instr.OpCode = opcode;
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

        #endregion
        
    }
}
