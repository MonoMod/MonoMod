using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Reflection;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        #region Misc Helpers

        public static bool Is(this MemberReference member, string fullName)
        {
            Helpers.ThrowIfArgumentNull(fullName);
            if (member == null)
                return false;
            return member.FullName.Replace("+", "/", StringComparison.Ordinal) == fullName.Replace("+", "/", StringComparison.Ordinal);
        }

        public static bool Is(this MemberReference member, string typeFullName, string name)
        {
            Helpers.ThrowIfArgumentNull(typeFullName);
            Helpers.ThrowIfArgumentNull(name);
            if (member == null)
                return false;
            return member.DeclaringType.FullName.Replace("+", "/", StringComparison.Ordinal) == typeFullName.Replace("+", "/", StringComparison.Ordinal) && member.Name == name;
        }

        public static bool Is(this MemberReference member, Type type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            Helpers.ThrowIfArgumentNull(name);
            if (member == null)
                return false;
            return member.DeclaringType.FullName.Replace("+", "/", StringComparison.Ordinal) == type.FullName?.Replace("+", "/", StringComparison.Ordinal)
                && member.Name == name;
        }

        public static bool Is(this MethodReference method, string fullName)
        {
            Helpers.ThrowIfArgumentNull(fullName);
            if (method == null)
                return false;

            if (fullName.Contains(' ', StringComparison.Ordinal))
            {
                // Namespace.Type::MethodName
                if (method.GetID(withType: true, simple: true).Replace("+", "/", StringComparison.Ordinal) == fullName.Replace("+", "/", StringComparison.Ordinal))
                    return true;

                // ReturnType Namespace.Type::MethodName(ArgType,ArgType)
                if (method.GetID().Replace("+", "/", StringComparison.Ordinal) == fullName.Replace("+", "/", StringComparison.Ordinal))
                    return true;
            }

            return method.FullName.Replace("+", "/", StringComparison.Ordinal) == fullName.Replace("+", "/", StringComparison.Ordinal);
        }

        public static bool Is(this MethodReference method, string typeFullName, string name)
        {
            Helpers.ThrowIfArgumentNull(typeFullName);
            Helpers.ThrowIfArgumentNull(name);
            if (method == null)
                return false;

            if (name.Contains(' ', StringComparison.Ordinal))
            {
                // ReturnType MethodName(ArgType,ArgType)
                if (method.DeclaringType.FullName.Replace("+", "/", StringComparison.Ordinal) == typeFullName.Replace("+", "/", StringComparison.Ordinal) && method.GetID(withType: false).Replace("+", "/", StringComparison.Ordinal) == name.Replace("+", "/", StringComparison.Ordinal))
                    return true;
            }

            return method.DeclaringType.FullName.Replace("+", "/", StringComparison.Ordinal) == typeFullName.Replace("+", "/", StringComparison.Ordinal) && method.Name == name;
        }

        public static bool Is(this MethodReference method, Type type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            Helpers.ThrowIfArgumentNull(name);
            if (method == null)
                return false;

            if (name.Contains(' ', StringComparison.Ordinal))
            {
                // ReturnType MethodName(ArgType,ArgType)
                if (method.DeclaringType.FullName.Replace("+", "/", StringComparison.Ordinal) == type.FullName?.Replace("+", "/", StringComparison.Ordinal)
                    && method.GetID(withType: false).Replace("+", "/", StringComparison.Ordinal) == name.Replace("+", "/", StringComparison.Ordinal))
                    return true;
            }

            return method.DeclaringType.FullName.Replace("+", "/", StringComparison.Ordinal) == type.FullName?.Replace("+", "/", StringComparison.Ordinal)
                && method.Name == name;
        }

        #endregion

        #region Misc IL Helpers

        public static void ReplaceOperands(this ILProcessor il, object? from, object? to)
        {
            Helpers.ThrowIfArgumentNull(il);
            foreach (var instr in il.Body.Instructions)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
        }

        #endregion

        #region Base Create / Emit Helpers

        public static FieldReference Import(this ILProcessor il, FieldInfo field)
            => Helpers.ThrowIfNull(il).Body.Method.Module.ImportReference(field);
        public static MethodReference Import(this ILProcessor il, MethodBase method)
            => Helpers.ThrowIfNull(il).Body.Method.Module.ImportReference(method);
        public static TypeReference Import(this ILProcessor il, Type type)
            => Helpers.ThrowIfNull(il).Body.Method.Module.ImportReference(type);
        public static MemberReference Import(this ILProcessor il, MemberInfo member)
        {
            Helpers.ThrowIfArgumentNull(il);
            Helpers.ThrowIfArgumentNull(member);
            switch (member)
            {
                case FieldInfo info:
                    return il.Import(info);
                case MethodBase info:
                    return il.Import(info);
                case Type info:
                    return il.Import(info);
                default:
                    throw new NotSupportedException("Unsupported member type " + member.GetType().FullName);
            }
        }

        public static Instruction Create(this ILProcessor il, OpCode opcode, FieldInfo field)
            => Helpers.ThrowIfNull(il).Create(opcode, il.Import(field));
        public static Instruction Create(this ILProcessor il, OpCode opcode, MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(il);
            if (method is System.Reflection.Emit.DynamicMethod)
                return il.Create(opcode, (object)method);
            return il.Create(opcode, il.Import(method));
        }
        public static Instruction Create(this ILProcessor il, OpCode opcode, Type type)
            => Helpers.ThrowIfNull(il).Create(opcode, il.Import(type));
        public static Instruction Create(this ILProcessor il, OpCode opcode, object operand)
        {
            var instr = Helpers.ThrowIfNull(il).Create(OpCodes.Nop);
            instr.OpCode = opcode;
            instr.Operand = operand;
            return instr;
        }
        public static Instruction Create(this ILProcessor il, OpCode opcode, MemberInfo member)
        {
            Helpers.ThrowIfArgumentNull(il);
            Helpers.ThrowIfArgumentNull(member);
            switch (member)
            {
                case FieldInfo info:
                    return il.Create(opcode, info);
                case MethodBase info:
                    return il.Create(opcode, info);
                case Type info:
                    return il.Create(opcode, info);
                default:
                    throw new NotSupportedException("Unsupported member type " + member.GetType().FullName);
            }
        }

        public static void Emit(this ILProcessor il, OpCode opcode, FieldInfo field)
            => Helpers.ThrowIfNull(il).Emit(opcode, il.Import(field));
        public static void Emit(this ILProcessor il, OpCode opcode, MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(il);
            Helpers.ThrowIfArgumentNull(method);
            if (method is System.Reflection.Emit.DynamicMethod)
            {
                il.Emit(opcode, (object)method);
                return;
            }
            il.Emit(opcode, il.Import(method));
        }
        public static void Emit(this ILProcessor il, OpCode opcode, Type type)
            => Helpers.ThrowIfNull(il).Emit(opcode, il.Import(type));
        public static void Emit(this ILProcessor il, OpCode opcode, MemberInfo member)
        {
            Helpers.ThrowIfArgumentNull(il);
            Helpers.ThrowIfArgumentNull(member);
            switch (member)
            {
                case FieldInfo info:
                    il.Emit(opcode, info);
                    break;
                case MethodBase info:
                    il.Emit(opcode, info);
                    break;
                case Type info:
                    il.Emit(opcode, info);
                    break;
                default:
                    throw new NotSupportedException("Unsupported member type " + member.GetType().FullName);
            }
        }
        public static void Emit(this ILProcessor il, OpCode opcode, object operand)
            => Helpers.ThrowIfNull(il).Append(il.Create(opcode, operand));

        #endregion

    }
}
