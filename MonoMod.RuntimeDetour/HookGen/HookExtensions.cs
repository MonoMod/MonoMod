using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using System.ComponentModel;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour.HookGen {
    public static class HookExtensions {

        public static Instruction Create(this ILProcessor il, OpCode opcode, FieldInfo field)
            => il.Create(opcode, il.Body.Method.Module.ImportReference(field));
        public static Instruction Create(this ILProcessor il, OpCode opcode, MethodBase method)
            => il.Create(opcode, il.Body.Method.Module.ImportReference(method));
        public static Instruction Create(this ILProcessor il, OpCode opcode, TypeReference type)
            => il.Create(opcode, il.Body.Method.Module.ImportReference(type));

        public static void Emit(this ILProcessor il, OpCode opcode, FieldReference field)
            => il.Emit(opcode, il.Body.Method.Module.ImportReference(field));
        public static void Emit(this ILProcessor il, OpCode opcode, MethodReference method)
            => il.Emit(opcode, il.Body.Method.Module.ImportReference(method));
        public static void Emit(this ILProcessor il, OpCode opcode, TypeReference type)
            => il.Emit(opcode, il.Body.Method.Module.ImportReference(type));

    }
}
