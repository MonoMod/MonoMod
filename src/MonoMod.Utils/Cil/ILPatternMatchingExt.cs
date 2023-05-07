#pragma warning disable IDE0008 // Use explicit type

using System;
using System.Reflection;
using MonoMod.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;
using MonoMod.SourceGen.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Cil {
    [EmitILOverloads("ILOpcodes.txt", ILOverloadKind.Matcher)]
    public static partial class ILPatternMatchingExt {
        #region Equivalence definitions
        private static bool IsEquivalent(int l, int r) => l == r;
        private static bool IsEquivalent(int l, uint r) => unchecked((uint) l) == r;
        private static bool IsEquivalent(long l, long r) => l == r;
        private static bool IsEquivalent(long l, ulong r) => unchecked((ulong) l) == r;
        private static bool IsEquivalent(float l, float r) => l == r;
        private static bool IsEquivalent(double l, double r) => l == r;
        private static bool IsEquivalent(string l, string r) => l == r;

        private static bool IsEquivalent(ILLabel l, ILLabel r) => l == r;
        private static bool IsEquivalent(ILLabel l, Instruction r) => IsEquivalent(l.Target, r);
        private static bool IsEquivalent(Instruction? l, Instruction? r) => l == r;

        private static bool IsEquivalent(TypeReference l, TypeReference r) => l == r;
        private static bool IsEquivalent(TypeReference l, Type r) => l.Is(r);

        private static bool IsEquivalent(MethodReference l, MethodReference r) => l == r;
        private static bool IsEquivalent(MethodReference l, MethodBase r) => l.Is(r);
        private static bool IsEquivalent(MethodReference l, Type type, string name)
            => l.DeclaringType.Is(type) && l.Name == name;

        private static bool IsEquivalent(FieldReference l, FieldReference r) => l == r;
        private static bool IsEquivalent(FieldReference l, FieldInfo r) => l.Is(r);
        private static bool IsEquivalent(FieldReference l, Type type, string name)
            => l.DeclaringType.Is(type) && l.Name == name;

        private static bool IsEquivalent(ILLabel[] l, ILLabel[] r)
            => l == r || l.SequenceEqual(r);
        private static bool IsEquivalent(ILLabel[] l, Instruction[] r) {
            if (l.Length != r.Length)
                return false;
            for (var i = 0; i < l.Length; i++) {
                if (!IsEquivalent(l[i].Target, r[i]))
                    return false;
            }
            return true;
        }

        private static bool IsEquivalent(IMethodSignature l, IMethodSignature r)
            => l == r; // TODO: is this valid?
        private static bool IsEquivalent(IMetadataTokenProvider l, IMetadataTokenProvider r)
            => l == r; // TODO: is this valid?
        private static bool IsEquivalent(IMetadataTokenProvider l, Type r)
            => l == r; // TODO: is this valid?
        private static bool IsEquivalent(IMetadataTokenProvider l, FieldInfo r)
            => l == r;
        private static bool IsEquivalent(IMetadataTokenProvider l, MethodBase r)
            => l == r;
        #endregion

        public static bool MatchLdarg(this Instruction instr, out int argIndex) {
            Helpers.ThrowIfArgumentNull(instr);
            if (instr.OpCode == OpCodes.Ldarg) {
                argIndex = (int)instr.Operand;
                return true;
            } else if (instr.OpCode == OpCodes.Ldarg_S) {
                argIndex = (byte)instr.Operand;
                return true;
            } else if (instr.OpCode == OpCodes.Ldarg_0) {
                argIndex = 0;
                return true;
            } else if (instr.OpCode == OpCodes.Ldarg_1) {
                argIndex = 1;
                return true;
            } else if (instr.OpCode == OpCodes.Ldarg_2) {
                argIndex = 2;
                return true;
            } else if (instr.OpCode == OpCodes.Ldarg_3) {
                argIndex = 3;
                return true;
            } else {
                argIndex = default;
                return false;
            }
        }

        public static bool MatchLdcI4(this Instruction instr, out int value) {
            Helpers.ThrowIfArgumentNull(instr);
            if (instr.OpCode == OpCodes.Ldc_I4) {
                value = (int) instr.Operand;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_S) {
                value = (byte) instr.Operand;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_0) {
                value = 0;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_1) {
                value = 1;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_2) {
                value = 2;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_3) {
                value = 3;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_4) {
                value = 4;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_5) {
                value = 5;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_6) {
                value = 6;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_7) {
                value = 7;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_8) {
                value = 8;
                return true;
            } else if (instr.OpCode == OpCodes.Ldc_I4_M1) {
                value = -1;
                return true;
            } else {
                value = default;
                return false;
            }
        }
    }
}
