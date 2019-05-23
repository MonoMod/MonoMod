#pragma warning disable IDE0008 // Use explicit type

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
using MonoMod.Cil;

namespace MonoMod.Cil {
    public static class ILPatternMatchingExt {

        public static bool Match(this Instruction instr, OpCode opcode)
            => instr.OpCode == opcode;
        public static bool Match<T>(this Instruction instr, OpCode opcode, T value)
            => instr.Match(opcode, out T v) && (v?.Equals(value) ?? value == null);
        public static bool Match<T>(this Instruction instr, OpCode opcode, out T value) {
            if (instr.OpCode == opcode) {
                value = (T) instr.Operand;
                return false;
            }
            value = default;
            return false;
        }

        public static bool MatchNop(this Instruction instr) {
            if (instr.OpCode == OpCodes.Nop) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchBreak(this Instruction instr) {
            if (instr.OpCode == OpCodes.Break) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdarg instead.", true)]
        public static bool MatchLdarg0(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldarg_0) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdarg instead.", true)]
        public static bool MatchLdarg1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldarg_1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdarg instead.", true)]
        public static bool MatchLdarg2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldarg_2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdarg instead.", true)]
        public static bool MatchLdarg3(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldarg_3) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdloc instead.", true)]
        public static bool MatchLdloc0(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldloc_0) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdloc instead.", true)]
        public static bool MatchLdloc1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldloc_1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdloc instead.", true)]
        public static bool MatchLdloc2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldloc_2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchLdloc instead.", true)]
        public static bool MatchLdloc3(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldloc_3) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchStloc instead.", true)]
        public static bool MatchStloc0(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stloc_0) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchStloc instead.", true)]
        public static bool MatchStloc1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stloc_1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchStloc instead.", true)]
        public static bool MatchStloc2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stloc_2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use MatchStloc instead.", true)]
        public static bool MatchStloc3(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stloc_3) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        // Merged into MatchLdarg
        /*
        public static bool MatchLdargS(this Instruction instr, ParameterReference value)
            => instr.MatchLdargS(out var v) && v == value;
        public static bool MatchLdargS(this Instruction instr, out ParameterReference value) {
            if (instr.OpCode == OpCodes.Ldarg_S) {
                value = (ParameterReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineArg
        */

        // Merged into MatchLdarga
        /*
        public static bool MatchLdargaS(this Instruction instr, ParameterReference value)
            => instr.MatchLdargaS(out var v) && v == value;
        public static bool MatchLdargaS(this Instruction instr, out ParameterReference value) {
            if (instr.OpCode == OpCodes.Ldarga_S) {
                value = (ParameterReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineArg
        */

        // Merged into MatchStarg
        /*
        public static bool MatchStargS(this Instruction instr, ParameterReference value)
            => instr.MatchStargS(out var v) && v == value;
        public static bool MatchStargS(this Instruction instr, out ParameterReference value) {
            if (instr.OpCode == OpCodes.Starg_S) {
                value = (ParameterReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineArg
        */

        // Merged into MatchLdloc
        /*
        public static bool MatchLdlocS(this Instruction instr, VariableReference value)
            => instr.MatchLdlocS(out var v) && v == value;
        public static bool MatchLdlocS(this Instruction instr, out VariableReference value) {
            if (instr.OpCode == OpCodes.Ldloc_S) {
                value = (VariableReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineVar
        */

        // Merged into MatchLdloca
        /*
        public static bool MatchLdlocaS(this Instruction instr, VariableReference value)
            => instr.MatchLdlocaS(out var v) && v == value;
        public static bool MatchLdlocaS(this Instruction instr, out VariableReference value) {
            if (instr.OpCode == OpCodes.Ldloca_S) {
                value = (VariableReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineVar
        */

        // Merged into MatchStloc
        /*
        public static bool MatchStlocS(this Instruction instr, VariableReference value)
            => instr.MatchStlocS(out var v) && v == value;
        public static bool MatchStlocS(this Instruction instr, out VariableReference value) {
            if (instr.OpCode == OpCodes.Stloc_S) {
                value = (VariableReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineVar
        */

        public static bool MatchLdnull(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldnull) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        // Merged into MatchLdcI4
        /*
        public static bool MatchLdcI4M1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_M1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI40(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_0) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI41(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI42(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI43(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_3) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI44(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI45(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_5) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI46(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_6) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI47(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_7) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI48(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldc_I4_8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdcI4S(this Instruction instr, sbyte value)
            => instr.MatchLdcI4S(out var v) && v == value;
        public static bool MatchLdcI4S(this Instruction instr, out sbyte value) {
            if (instr.OpCode == OpCodes.Ldc_I4_S) {
                value = (sbyte) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineI
        */

        public static bool MatchLdcI4(this Instruction instr, int value)
            => instr.MatchLdcI4(out var v) && v == value;
        public static bool MatchLdcI4(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Ldc_I4) { value = (int) instr.Operand; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_S) { value = (sbyte) instr.Operand; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_0) { value = 0; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_1) { value = 1; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_2) { value = 2; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_3) { value = 3; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_4) { value = 4; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_5) { value = 5; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_6) { value = 6; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_7) { value = 7; return true; }
            if (instr.OpCode == OpCodes.Ldc_I4_8) { value = 8; return true; }
            value = default;
            return false;
        } // OperandType.InlineI

        public static bool MatchLdcI8(this Instruction instr, long value)
            => instr.MatchLdcI8(out var v) && v == value;
        public static bool MatchLdcI8(this Instruction instr, out long value) {
            if (instr.OpCode == OpCodes.Ldc_I8) {
                value = (long) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineI8

        public static bool MatchLdcR4(this Instruction instr, float value)
            => instr.MatchLdcR4(out var v) && v == value;
        public static bool MatchLdcR4(this Instruction instr, out float value) {
            if (instr.OpCode == OpCodes.Ldc_R4) {
                value = (float) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineR

        public static bool MatchLdcR8(this Instruction instr, double value)
            => instr.MatchLdcR8(out var v) && v == value;
        public static bool MatchLdcR8(this Instruction instr, out double value) {
            if (instr.OpCode == OpCodes.Ldc_R8) {
                value = (double) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineR

        public static bool MatchDup(this Instruction instr) {
            if (instr.OpCode == OpCodes.Dup) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchPop(this Instruction instr) {
            if (instr.OpCode == OpCodes.Pop) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchJmp(this Instruction instr, string typeFullName, string name)
            => instr.MatchJmp(out var v) && v.Is(typeFullName, name);
        public static bool MatchJmp<T>(this Instruction instr, string name)
            => instr.MatchJmp(out var v) && v.Is(typeof(T), name);
        public static bool MatchJmp(this Instruction instr, Type type, string name)
            => instr.MatchJmp(out var v) && v.Is(type, name);
        public static bool MatchJmp(this Instruction instr, MethodBase value)
            => instr.MatchJmp(out var v) && v.Is(value);
        public static bool MatchJmp(this Instruction instr, MethodReference value)
            => instr.MatchJmp(out var v) && v == value;
        public static bool MatchJmp(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Jmp) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchCall(this Instruction instr, string typeFullName, string name)
            => instr.MatchCall(out var v) && v.Is(typeFullName, name);
        public static bool MatchCall<T>(this Instruction instr, string name)
            => instr.MatchCall(out var v) && v.Is(typeof(T), name);
        public static bool MatchCall(this Instruction instr, Type type, string name)
            => instr.MatchCall(out var v) && v.Is(type, name);
        public static bool MatchCall(this Instruction instr, MethodBase value)
            => instr.MatchCall(out var v) && v.Is(value);
        public static bool MatchCall(this Instruction instr, MethodReference value)
            => instr.MatchCall(out var v) && v == value;
        public static bool MatchCall(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Call) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchCallvirt(this Instruction instr, string typeFullName, string name)
            => instr.MatchCallvirt(out var v) && v.Is(typeFullName, name);
        public static bool MatchCallvirt<T>(this Instruction instr, string name)
            => instr.MatchCallvirt(out var v) && v.Is(typeof(T), name);
        public static bool MatchCallvirt(this Instruction instr, Type type, string name)
            => instr.MatchCallvirt(out var v) && v.Is(type, name);
        public static bool MatchCallvirt(this Instruction instr, MethodBase value)
            => instr.MatchCallvirt(out var v) && v.Is(value);
        public static bool MatchCallvirt(this Instruction instr, MethodReference value)
            => instr.MatchCallvirt(out var v) && v == value;
        public static bool MatchCallvirt(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Callvirt) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchCallOrCallvirt(this Instruction instr, string typeFullName, string name)
            => instr.MatchCallOrCallvirt(out var v) && v.Is(typeFullName, name);
        public static bool MatchCallOrCallvirt<T>(this Instruction instr, string name)
            => instr.MatchCallOrCallvirt(out var v) && v.Is(typeof(T), name);
        public static bool MatchCallOrCallvirt(this Instruction instr, Type type, string name)
            => instr.MatchCallOrCallvirt(out var v) && v.Is(type, name);
        public static bool MatchCallOrCallvirt(this Instruction instr, MethodBase value)
            => instr.MatchCallOrCallvirt(out var v) && v.Is(value);
        public static bool MatchCallOrCallvirt(this Instruction instr, MethodReference value)
            => instr.MatchCallOrCallvirt(out var v) && v == value;
        public static bool MatchCallOrCallvirt(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchCalli(this Instruction instr, IMethodSignature value)
            => instr.MatchCalli(out var v) && v == value;
        public static bool MatchCalli(this Instruction instr, out IMethodSignature value) {
            if (instr.OpCode == OpCodes.Calli) {
                value = (IMethodSignature) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineSig

        public static bool MatchRet(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ret) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        // Merged into MatchBr*
        /*
        public static bool MatchBrS(this Instruction instr, ILLabel value)
            => instr.MatchBrS(out var v) && v == value;
        public static bool MatchBrS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Br_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBrfalseS(this Instruction instr, ILLabel value)
            => instr.MatchBrfalseS(out var v) && v == value;
        public static bool MatchBrfalseS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Brfalse_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBrtrueS(this Instruction instr, ILLabel value)
            => instr.MatchBrtrueS(out var v) && v == value;
        public static bool MatchBrtrueS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Brtrue_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBeqS(this Instruction instr, ILLabel value)
            => instr.MatchBeqS(out var v) && v == value;
        public static bool MatchBeqS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Beq_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBgeS(this Instruction instr, ILLabel value)
            => instr.MatchBgeS(out var v) && v == value;
        public static bool MatchBgeS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bge_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBgtS(this Instruction instr, ILLabel value)
            => instr.MatchBgtS(out var v) && v == value;
        public static bool MatchBgtS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bgt_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBleS(this Instruction instr, ILLabel value)
            => instr.MatchBleS(out var v) && v == value;
        public static bool MatchBleS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Ble_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBltS(this Instruction instr, ILLabel value)
            => instr.MatchBltS(out var v) && v == value;
        public static bool MatchBltS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Blt_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBneUnS(this Instruction instr, ILLabel value)
            => instr.MatchBneUnS(out var v) && v == value;
        public static bool MatchBneUnS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bne_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBgeUnS(this Instruction instr, ILLabel value)
            => instr.MatchBgeUnS(out var v) && v == value;
        public static bool MatchBgeUnS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bge_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBgtUnS(this Instruction instr, ILLabel value)
            => instr.MatchBgtUnS(out var v) && v == value;
        public static bool MatchBgtUnS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bgt_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBleUnS(this Instruction instr, ILLabel value)
            => instr.MatchBleUnS(out var v) && v == value;
        public static bool MatchBleUnS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Ble_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchBltUnS(this Instruction instr, ILLabel value)
            => instr.MatchBltUnS(out var v) && v == value;
        public static bool MatchBltUnS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Blt_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget
        */

        public static bool MatchBr(this Instruction instr, ILLabel value)
            => instr.MatchBr(out var v) && v == value;
        public static bool MatchBr(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Br ||
                instr.OpCode == OpCodes.Br_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBrfalse(this Instruction instr, ILLabel value)
            => instr.MatchBrfalse(out var v) && v == value;
        public static bool MatchBrfalse(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Brfalse ||
                instr.OpCode == OpCodes.Brfalse_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBrtrue(this Instruction instr, ILLabel value)
            => instr.MatchBrtrue(out var v) && v == value;
        public static bool MatchBrtrue(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Brtrue ||
                instr.OpCode == OpCodes.Brtrue_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBeq(this Instruction instr, ILLabel value)
            => instr.MatchBeq(out var v) && v == value;
        public static bool MatchBeq(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Beq ||
                instr.OpCode == OpCodes.Beq_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBge(this Instruction instr, ILLabel value)
            => instr.MatchBge(out var v) && v == value;
        public static bool MatchBge(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bge ||
                instr.OpCode == OpCodes.Bge_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBgt(this Instruction instr, ILLabel value)
            => instr.MatchBgt(out var v) && v == value;
        public static bool MatchBgt(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bgt ||
                instr.OpCode == OpCodes.Bgt_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBle(this Instruction instr, ILLabel value)
            => instr.MatchBle(out var v) && v == value;
        public static bool MatchBle(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Ble ||
                instr.OpCode == OpCodes.Ble_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBlt(this Instruction instr, ILLabel value)
            => instr.MatchBlt(out var v) && v == value;
        public static bool MatchBlt(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Blt ||
                instr.OpCode == OpCodes.Blt_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBneUn(this Instruction instr, ILLabel value)
            => instr.MatchBneUn(out var v) && v == value;
        public static bool MatchBneUn(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bne_Un ||
                instr.OpCode == OpCodes.Bne_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBgeUn(this Instruction instr, ILLabel value)
            => instr.MatchBgeUn(out var v) && v == value;
        public static bool MatchBgeUn(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bge_Un ||
                instr.OpCode == OpCodes.Bge_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBgtUn(this Instruction instr, ILLabel value)
            => instr.MatchBgtUn(out var v) && v == value;
        public static bool MatchBgtUn(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Bgt_Un ||
                instr.OpCode == OpCodes.Bgt_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBleUn(this Instruction instr, ILLabel value)
            => instr.MatchBleUn(out var v) && v == value;
        public static bool MatchBleUn(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Ble_Un ||
                instr.OpCode == OpCodes.Ble_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchBltUn(this Instruction instr, ILLabel value)
            => instr.MatchBltUn(out var v) && v == value;
        public static bool MatchBltUn(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Blt_Un ||
                instr.OpCode == OpCodes.Blt_Un_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchSwitch(this Instruction instr, ILLabel[] value)
            => instr.MatchSwitch(out var v) && v.SequenceEqual(value);
        public static bool MatchSwitch(this Instruction instr, out ILLabel[] value) {
            if (instr.OpCode == OpCodes.Switch) {
                value = (ILLabel[]) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineSwitch

        public static bool MatchLdindI1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_I1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindU1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_U1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindI2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_I2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindU2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_U2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindI4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_I4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindU4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_U4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindI8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_I8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindI(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_I) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindR4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_R4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindR8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_R8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdindRef(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldind_Ref) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindRef(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_Ref) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindI1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_I1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindI2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_I2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindI4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_I4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindI8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_I8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindR4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_R4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStindR8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_R8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchAdd(this Instruction instr) {
            if (instr.OpCode == OpCodes.Add) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchSub(this Instruction instr) {
            if (instr.OpCode == OpCodes.Sub) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchMul(this Instruction instr) {
            if (instr.OpCode == OpCodes.Mul) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchDiv(this Instruction instr) {
            if (instr.OpCode == OpCodes.Div) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchDivUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Div_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchRem(this Instruction instr) {
            if (instr.OpCode == OpCodes.Rem) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchRemUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Rem_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchAnd(this Instruction instr) {
            if (instr.OpCode == OpCodes.And) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchOr(this Instruction instr) {
            if (instr.OpCode == OpCodes.Or) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchXor(this Instruction instr) {
            if (instr.OpCode == OpCodes.Xor) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchShl(this Instruction instr) {
            if (instr.OpCode == OpCodes.Shl) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchShr(this Instruction instr) {
            if (instr.OpCode == OpCodes.Shr) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchShrUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Shr_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchNeg(this Instruction instr) {
            if (instr.OpCode == OpCodes.Neg) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchNot(this Instruction instr) {
            if (instr.OpCode == OpCodes.Not) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvI1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_I1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvI2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_I2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvI4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_I4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvI8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_I8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvR4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_R4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvR8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_R8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvU4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_U4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvU8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_U8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchCpobj(this Instruction instr, string fullName)
            => instr.MatchCpobj(out var v) && v.Is(fullName);
        public static bool MatchCpobj<T>(this Instruction instr)
            => instr.MatchCpobj(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchCpobj(this Instruction instr, Type value)
            => instr.MatchCpobj(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchCpobj(this Instruction instr, TypeReference value)
            => instr.MatchCpobj(out var v) && v == value;
        public static bool MatchCpobj(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Cpobj) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchLdobj(this Instruction instr, string fullName)
            => instr.MatchLdobj(out var v) && v.Is(fullName);
        public static bool MatchLdobj<T>(this Instruction instr)
            => instr.MatchLdobj(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchLdobj(this Instruction instr, Type value)
            => instr.MatchLdobj(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchLdobj(this Instruction instr, TypeReference value)
            => instr.MatchLdobj(out var v) && v == value;
        public static bool MatchLdobj(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Ldobj) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchLdstr(this Instruction instr, string value)
            => instr.MatchLdstr(out var v) && v == value;
        public static bool MatchLdstr(this Instruction instr, out string value) {
            if (instr.OpCode == OpCodes.Ldstr) {
                value = (string) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineString

        // Start special cases for Newobj
        public static bool MatchNewobj(this Instruction instr, string typeFullName)
            => instr.MatchNewobj(out var v) && v.DeclaringType.Is(typeFullName);
        public static bool MatchNewobj<T>(this Instruction instr)
            => instr.MatchNewobj(out var v) && v.DeclaringType.Is(typeof(T).GetTypeInfo());
        public static bool MatchNewobj(this Instruction instr, Type type)
            => instr.MatchNewobj(out var v) && v.DeclaringType.Is(type.GetTypeInfo());
        /*
        public static bool MatchNewobj(this Instruction instr, string typeFullName, string name)
            => instr.MatchNewobj(out var v) && v.Is(typeFullName, name);
        public static bool MatchNewobj<T>(this Instruction instr, string name)
            => instr.MatchNewobj(out var v) && v.Is(typeof(T), name);
        public static bool MatchNewobj(this Instruction instr, Type type, string name)
            => instr.MatchNewobj(out var v) && v.Is(type, name);
        */
        // End special cases for Newobj
        public static bool MatchNewobj(this Instruction instr, MethodBase value)
            => instr.MatchNewobj(out var v) && v.Is(value);
        public static bool MatchNewobj(this Instruction instr, MethodReference value)
            => instr.MatchNewobj(out var v) && v == value;
        public static bool MatchNewobj(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Newobj) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchCastclass(this Instruction instr, string fullName)
            => instr.MatchCastclass(out var v) && v.Is(fullName);
        public static bool MatchCastclass<T>(this Instruction instr)
            => instr.MatchCastclass(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchCastclass(this Instruction instr, Type value)
            => instr.MatchCastclass(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchCastclass(this Instruction instr, TypeReference value)
            => instr.MatchCastclass(out var v) && v == value;
        public static bool MatchCastclass(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Castclass) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchIsinst(this Instruction instr, string fullName)
            => instr.MatchIsinst(out var v) && v.Is(fullName);
        public static bool MatchIsinst<T>(this Instruction instr)
            => instr.MatchIsinst(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchIsinst(this Instruction instr, Type value)
            => instr.MatchIsinst(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchIsinst(this Instruction instr, TypeReference value)
            => instr.MatchIsinst(out var v) && v == value;
        public static bool MatchIsinst(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Isinst) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchConvRUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_R_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchUnbox(this Instruction instr, string fullName)
            => instr.MatchUnbox(out var v) && v.Is(fullName);
        public static bool MatchUnbox<T>(this Instruction instr)
            => instr.MatchUnbox(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchUnbox(this Instruction instr, Type value)
            => instr.MatchUnbox(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchUnbox(this Instruction instr, TypeReference value)
            => instr.MatchUnbox(out var v) && v == value;
        public static bool MatchUnbox(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Unbox) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchThrow(this Instruction instr) {
            if (instr.OpCode == OpCodes.Throw) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdfld(this Instruction instr, string typeFullName, string name)
            => instr.MatchLdfld(out var v) && v.Is(typeFullName, name);
        public static bool MatchLdfld<T>(this Instruction instr, string name)
            => instr.MatchLdfld(out var v) && v.Is(typeof(T), name);
        public static bool MatchLdfld(this Instruction instr, Type type, string name)
            => instr.MatchLdfld(out var v) && v.Is(type, name);
        public static bool MatchLdfld(this Instruction instr, FieldInfo value)
            => instr.MatchLdfld(out var v) && v.Is(value);
        public static bool MatchLdfld(this Instruction instr, FieldReference value)
            => instr.MatchLdfld(out var v) && v == value;
        public static bool MatchLdfld(this Instruction instr, out FieldReference value) {
            if (instr.OpCode == OpCodes.Ldfld) {
                value = (FieldReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineField

        public static bool MatchLdflda(this Instruction instr, string typeFullName, string name)
            => instr.MatchLdflda(out var v) && v.Is(typeFullName, name);
        public static bool MatchLdflda<T>(this Instruction instr, string name)
            => instr.MatchLdflda(out var v) && v.Is(typeof(T), name);
        public static bool MatchLdflda(this Instruction instr, Type type, string name)
            => instr.MatchLdflda(out var v) && v.Is(type, name);
        public static bool MatchLdflda(this Instruction instr, FieldInfo value)
            => instr.MatchLdflda(out var v) && v.Is(value);
        public static bool MatchLdflda(this Instruction instr, FieldReference value)
            => instr.MatchLdflda(out var v) && v == value;
        public static bool MatchLdflda(this Instruction instr, out FieldReference value) {
            if (instr.OpCode == OpCodes.Ldflda) {
                value = (FieldReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineField

        public static bool MatchStfld(this Instruction instr, string typeFullName, string name)
            => instr.MatchStfld(out var v) && v.Is(typeFullName, name);
        public static bool MatchStfld<T>(this Instruction instr, string name)
            => instr.MatchStfld(out var v) && v.Is(typeof(T), name);
        public static bool MatchStfld(this Instruction instr, Type type, string name)
            => instr.MatchStfld(out var v) && v.Is(type, name);
        public static bool MatchStfld(this Instruction instr, FieldInfo value)
            => instr.MatchStfld(out var v) && v.Is(value);
        public static bool MatchStfld(this Instruction instr, FieldReference value)
            => instr.MatchStfld(out var v) && v == value;
        public static bool MatchStfld(this Instruction instr, out FieldReference value) {
            if (instr.OpCode == OpCodes.Stfld) {
                value = (FieldReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineField

        public static bool MatchLdsfld(this Instruction instr, string typeFullName, string name)
            => instr.MatchLdsfld(out var v) && v.Is(typeFullName, name);
        public static bool MatchLdsfld<T>(this Instruction instr, string name)
            => instr.MatchLdsfld(out var v) && v.Is(typeof(T), name);
        public static bool MatchLdsfld(this Instruction instr, Type type, string name)
            => instr.MatchLdsfld(out var v) && v.Is(type, name);
        public static bool MatchLdsfld(this Instruction instr, FieldInfo value)
            => instr.MatchLdsfld(out var v) && v.Is(value);
        public static bool MatchLdsfld(this Instruction instr, FieldReference value)
            => instr.MatchLdsfld(out var v) && v == value;
        public static bool MatchLdsfld(this Instruction instr, out FieldReference value) {
            if (instr.OpCode == OpCodes.Ldsfld) {
                value = (FieldReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineField

        public static bool MatchLdsflda(this Instruction instr, string typeFullName, string name)
            => instr.MatchLdsflda(out var v) && v.Is(typeFullName, name);
        public static bool MatchLdsflda<T>(this Instruction instr, string name)
            => instr.MatchLdsflda(out var v) && v.Is(typeof(T), name);
        public static bool MatchLdsflda(this Instruction instr, Type type, string name)
            => instr.MatchLdsflda(out var v) && v.Is(type, name);
        public static bool MatchLdsflda(this Instruction instr, FieldInfo value)
            => instr.MatchLdsflda(out var v) && v.Is(value);
        public static bool MatchLdsflda(this Instruction instr, FieldReference value)
            => instr.MatchLdsflda(out var v) && v == value;
        public static bool MatchLdsflda(this Instruction instr, out FieldReference value) {
            if (instr.OpCode == OpCodes.Ldsflda) {
                value = (FieldReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineField

        public static bool MatchStsfld(this Instruction instr, string typeFullName, string name)
            => instr.MatchStsfld(out var v) && v.Is(typeFullName, name);
        public static bool MatchStsfld<T>(this Instruction instr, string name)
            => instr.MatchStsfld(out var v) && v.Is(typeof(T), name);
        public static bool MatchStsfld(this Instruction instr, Type type, string name)
            => instr.MatchStsfld(out var v) && v.Is(type, name);
        public static bool MatchStsfld(this Instruction instr, FieldInfo value)
            => instr.MatchStsfld(out var v) && v.Is(value);
        public static bool MatchStsfld(this Instruction instr, FieldReference value)
            => instr.MatchStsfld(out var v) && v == value;
        public static bool MatchStsfld(this Instruction instr, out FieldReference value) {
            if (instr.OpCode == OpCodes.Stsfld) {
                value = (FieldReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineField

        public static bool MatchStobj(this Instruction instr, string fullName)
            => instr.MatchStobj(out var v) && v.Is(fullName);
        public static bool MatchStobj<T>(this Instruction instr)
            => instr.MatchStobj(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchStobj(this Instruction instr, Type value)
            => instr.MatchStobj(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchStobj(this Instruction instr, TypeReference value)
            => instr.MatchStobj(out var v) && v == value;
        public static bool MatchStobj(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Stobj) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchConvOvfI1Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I1_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfI2Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I2_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfI4Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I4_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfI8Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I8_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU1Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U1_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU2Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U2_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU4Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U4_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU8Un(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U8_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfIUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfUUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchBox(this Instruction instr, string fullName)
            => instr.MatchBox(out var v) && v.Is(fullName);
        public static bool MatchBox<T>(this Instruction instr)
            => instr.MatchBox(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchBox(this Instruction instr, Type value)
            => instr.MatchBox(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchBox(this Instruction instr, TypeReference value)
            => instr.MatchBox(out var v) && v == value;
        public static bool MatchBox(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Box) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchNewarr(this Instruction instr, string fullName)
            => instr.MatchNewarr(out var v) && v.Is(fullName);
        public static bool MatchNewarr<T>(this Instruction instr)
            => instr.MatchNewarr(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchNewarr(this Instruction instr, Type value)
            => instr.MatchNewarr(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchNewarr(this Instruction instr, TypeReference value)
            => instr.MatchNewarr(out var v) && v == value;
        public static bool MatchNewarr(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Newarr) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchLdlen(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldlen) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelema(this Instruction instr, string fullName)
            => instr.MatchLdelema(out var v) && v.Is(fullName);
        public static bool MatchLdelema<T>(this Instruction instr)
            => instr.MatchLdelema(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchLdelema(this Instruction instr, Type value)
            => instr.MatchLdelema(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchLdelema(this Instruction instr, TypeReference value)
            => instr.MatchLdelema(out var v) && v == value;
        public static bool MatchLdelema(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Ldelema) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchLdelemI1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_I1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemU1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_U1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemI2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_I2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemU2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_U2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemI4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_I4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemU4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_U4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemI8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_I8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemI(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_I) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemR4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_R4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemR8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_R8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemRef(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ldelem_Ref) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemI(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_I) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemI1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_I1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemI2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_I2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemI4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_I4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemI8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_I8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemR4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_R4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemR8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_R8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchStelemRef(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stelem_Ref) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdelemAny(this Instruction instr, string fullName)
            => instr.MatchLdelemAny(out var v) && v.Is(fullName);
        public static bool MatchLdelemAny<T>(this Instruction instr)
            => instr.MatchLdelemAny(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchLdelemAny(this Instruction instr, Type value)
            => instr.MatchLdelemAny(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchLdelemAny(this Instruction instr, TypeReference value)
            => instr.MatchLdelemAny(out var v) && v == value;
        public static bool MatchLdelemAny(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Ldelem_Any) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchStelemAny(this Instruction instr, string fullName)
            => instr.MatchStelemAny(out var v) && v.Is(fullName);
        public static bool MatchStelemAny<T>(this Instruction instr)
            => instr.MatchStelemAny(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchStelemAny(this Instruction instr, Type value)
            => instr.MatchStelemAny(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchStelemAny(this Instruction instr, TypeReference value)
            => instr.MatchStelemAny(out var v) && v == value;
        public static bool MatchStelemAny(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Stelem_Any) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchUnboxAny(this Instruction instr, string fullName)
            => instr.MatchUnboxAny(out var v) && v.Is(fullName);
        public static bool MatchUnboxAny<T>(this Instruction instr)
            => instr.MatchUnboxAny(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchUnboxAny(this Instruction instr, Type value)
            => instr.MatchUnboxAny(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchUnboxAny(this Instruction instr, TypeReference value)
            => instr.MatchUnboxAny(out var v) && v == value;
        public static bool MatchUnboxAny(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Unbox_Any) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchConvOvfI1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfI2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfI4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU4(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U4) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfI8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvOvfU8(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U8) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchRefanyval(this Instruction instr, string fullName)
            => instr.MatchRefanyval(out var v) && v.Is(fullName);
        public static bool MatchRefanyval<T>(this Instruction instr)
            => instr.MatchRefanyval(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchRefanyval(this Instruction instr, Type value)
            => instr.MatchRefanyval(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchRefanyval(this Instruction instr, TypeReference value)
            => instr.MatchRefanyval(out var v) && v == value;
        public static bool MatchRefanyval(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Refanyval) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchCkfinite(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ckfinite) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchMkrefany(this Instruction instr, string fullName)
            => instr.MatchMkrefany(out var v) && v.Is(fullName);
        public static bool MatchMkrefany<T>(this Instruction instr)
            => instr.MatchMkrefany(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchMkrefany(this Instruction instr, Type value)
            => instr.MatchMkrefany(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchMkrefany(this Instruction instr, TypeReference value)
            => instr.MatchMkrefany(out var v) && v == value;
        public static bool MatchMkrefany(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Mkrefany) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchLdtoken(this Instruction instr, IMetadataTokenProvider value)
            => instr.MatchLdtoken(out var v) && v == value;
        public static bool MatchLdtoken(this Instruction instr, out IMetadataTokenProvider value) {
            if (instr.OpCode == OpCodes.Ldtoken) {
                value = (IMetadataTokenProvider) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineTok

        public static bool MatchConvU2(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_U2) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvU1(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_U1) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvI(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_I) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConv_OvfI(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_I) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConv_OvfU(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_Ovf_U) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchAddOvf(this Instruction instr) {
            if (instr.OpCode == OpCodes.Add_Ovf) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchAdd_OvfUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Add_Ovf_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchMulOvf(this Instruction instr) {
            if (instr.OpCode == OpCodes.Mul_Ovf) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchMulOvfUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Mul_Ovf_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchSubOvf(this Instruction instr) {
            if (instr.OpCode == OpCodes.Sub_Ovf) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchSubOvfUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Sub_Ovf_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchEndfinally(this Instruction instr) {
            if (instr.OpCode == OpCodes.Endfinally) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLeave(this Instruction instr, ILLabel value)
            => instr.MatchLeave(out var v) && v == value;
        public static bool MatchLeave(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Leave) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineBrTarget

        public static bool MatchLeaveS(this Instruction instr, ILLabel value)
            => instr.MatchLeaveS(out var v) && v == value;
        public static bool MatchLeaveS(this Instruction instr, out ILLabel value) {
            if (instr.OpCode == OpCodes.Leave_S) {
                value = (ILLabel) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineBrTarget

        public static bool MatchStindI(this Instruction instr) {
            if (instr.OpCode == OpCodes.Stind_I) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchConvU(this Instruction instr) {
            if (instr.OpCode == OpCodes.Conv_U) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchArglist(this Instruction instr) {
            if (instr.OpCode == OpCodes.Arglist) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchCeq(this Instruction instr) {
            if (instr.OpCode == OpCodes.Ceq) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchCgt(this Instruction instr) {
            if (instr.OpCode == OpCodes.Cgt) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchCgtUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Cgt_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchClt(this Instruction instr) {
            if (instr.OpCode == OpCodes.Clt) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchCltUn(this Instruction instr) {
            if (instr.OpCode == OpCodes.Clt_Un) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchLdftn(this Instruction instr, string typeFullName, string name)
            => instr.MatchLdftn(out var v) && v.Is(typeFullName, name);
        public static bool MatchLdftn<T>(this Instruction instr, string name)
            => instr.MatchLdftn(out var v) && v.Is(typeof(T), name);
        public static bool MatchLdftn(this Instruction instr, Type type, string name)
            => instr.MatchLdftn(out var v) && v.Is(type, name);
        public static bool MatchLdftn(this Instruction instr, MethodBase value)
            => instr.MatchLdftn(out var v) && v.Is(value);
        public static bool MatchLdftn(this Instruction instr, MethodReference value)
            => instr.MatchLdftn(out var v) && v == value;
        public static bool MatchLdftn(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Ldftn) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchLdvirtftn(this Instruction instr, string typeFullName, string name)
            => instr.MatchLdvirtftn(out var v) && v.Is(typeFullName, name);
        public static bool MatchLdvirtftn<T>(this Instruction instr, string name)
            => instr.MatchLdvirtftn(out var v) && v.Is(typeof(T), name);
        public static bool MatchLdvirtftn(this Instruction instr, Type type, string name)
            => instr.MatchLdvirtftn(out var v) && v.Is(type, name);
        public static bool MatchLdvirtftn(this Instruction instr, MethodBase value)
            => instr.MatchLdvirtftn(out var v) && v.Is(value);
        public static bool MatchLdvirtftn(this Instruction instr, MethodReference value)
            => instr.MatchLdvirtftn(out var v) && v == value;
        public static bool MatchLdvirtftn(this Instruction instr, out MethodReference value) {
            if (instr.OpCode == OpCodes.Ldvirtftn) {
                value = instr.Operand as MethodReference;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineMethod

        public static bool MatchLdarg(this Instruction instr, int value)
            => instr.MatchLdarg(out var v) && v == value;
        public static bool MatchLdarg(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Ldarg ||
                instr.OpCode == OpCodes.Ldarg_S) {
                value = ((ParameterReference) instr.Operand).Index;
                return true;
            }
            if (instr.OpCode == OpCodes.Ldarg_0) { value = 0; return true; }
            if (instr.OpCode == OpCodes.Ldarg_1) { value = 1; return true; }
            if (instr.OpCode == OpCodes.Ldarg_2) { value = 2; return true; }
            if (instr.OpCode == OpCodes.Ldarg_3) { value = 3; return true; }
            value = default;
            return false;
        } // OperandType.InlineArg

        public static bool MatchLdarga(this Instruction instr, int value)
            => instr.MatchLdarga(out var v) && v == value;
        public static bool MatchLdarga(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Ldarga ||
                instr.OpCode == OpCodes.Ldarga_S) {
                value = ((ParameterReference) instr.Operand).Index;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineArg

        public static bool MatchStarg(this Instruction instr, int value)
            => instr.MatchStarg(out var v) && v == value;
        public static bool MatchStarg(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Starg ||
                instr.OpCode == OpCodes.Starg_S) {
                value = ((ParameterReference) instr.Operand).Index;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineArg

        public static bool MatchLdloc(this Instruction instr, int value)
            => instr.MatchLdloc(out var v) && v == value;
        public static bool MatchLdloc(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Ldloc ||
                instr.OpCode == OpCodes.Ldloc_S) {
                value = ((VariableReference) instr.Operand).Index;
                return true;
            }
            if (instr.OpCode == OpCodes.Ldloc_0) { value = 0; return true; }
            if (instr.OpCode == OpCodes.Ldloc_1) { value = 1; return true; }
            if (instr.OpCode == OpCodes.Ldloc_2) { value = 2; return true; }
            if (instr.OpCode == OpCodes.Ldloc_3) { value = 3; return true; }
            value = default;
            return false;
        } // OperandType.InlineVar

        public static bool MatchLdloca(this Instruction instr, int value)
            => instr.MatchLdloca(out var v) && v == value;
        public static bool MatchLdloca(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Ldloca ||
                instr.OpCode == OpCodes.Ldloca_S) {
                value = ((VariableReference) instr.Operand).Index;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineVar

        public static bool MatchStloc(this Instruction instr, int value)
            => instr.MatchStloc(out var v) && v == value;
        public static bool MatchStloc(this Instruction instr, out int value) {
            if (instr.OpCode == OpCodes.Stloc ||
                instr.OpCode == OpCodes.Stloc_S) {
                value = ((VariableReference) instr.Operand).Index;
                return true;
            }
            if (instr.OpCode == OpCodes.Stloc_0) { value = 0; return true; }
            if (instr.OpCode == OpCodes.Stloc_1) { value = 1; return true; }
            if (instr.OpCode == OpCodes.Stloc_2) { value = 2; return true; }
            if (instr.OpCode == OpCodes.Stloc_3) { value = 3; return true; }
            value = default;
            return false;
        } // OperandType.InlineVar

        public static bool MatchLocalloc(this Instruction instr) {
            if (instr.OpCode == OpCodes.Localloc) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchEndfilter(this Instruction instr) {
            if (instr.OpCode == OpCodes.Endfilter) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchUnaligned(this Instruction instr, sbyte value)
            => instr.MatchUnaligned(out var v) && v == value;
        public static bool MatchUnaligned(this Instruction instr, out sbyte value) {
            if (instr.OpCode == OpCodes.Unaligned) {
                value = (sbyte) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineI

        public static bool MatchVolatile(this Instruction instr) {
            if (instr.OpCode == OpCodes.Volatile) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchTail(this Instruction instr) {
            if (instr.OpCode == OpCodes.Tail) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchInitobj(this Instruction instr, string fullName)
            => instr.MatchInitobj(out var v) && v.Is(fullName);
        public static bool MatchInitobj<T>(this Instruction instr)
            => instr.MatchInitobj(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchInitobj(this Instruction instr, Type value)
            => instr.MatchInitobj(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchInitobj(this Instruction instr, TypeReference value)
            => instr.MatchInitobj(out var v) && v == value;
        public static bool MatchInitobj(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Initobj) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchConstrained(this Instruction instr, string fullName)
            => instr.MatchConstrained(out var v) && v.Is(fullName);
        public static bool MatchConstrained<T>(this Instruction instr)
            => instr.MatchConstrained(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchConstrained(this Instruction instr, Type value)
            => instr.MatchConstrained(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchConstrained(this Instruction instr, TypeReference value)
            => instr.MatchConstrained(out var v) && v == value;
        public static bool MatchConstrained(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Constrained) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchCpblk(this Instruction instr) {
            if (instr.OpCode == OpCodes.Cpblk) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchInitblk(this Instruction instr) {
            if (instr.OpCode == OpCodes.Initblk) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchNo(this Instruction instr, sbyte value)
            => instr.MatchNo(out var v) && v == value;
        public static bool MatchNo(this Instruction instr, out sbyte value) {
            if (instr.OpCode == OpCodes.No) {
                value = (sbyte) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.ShortInlineI

        public static bool MatchRethrow(this Instruction instr) {
            if (instr.OpCode == OpCodes.Rethrow) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchSizeof(this Instruction instr, string fullName)
            => instr.MatchSizeof(out var v) && v.Is(fullName);
        public static bool MatchSizeof<T>(this Instruction instr)
            => instr.MatchSizeof(out var v) && v.Is(typeof(T).GetTypeInfo());
        public static bool MatchSizeof(this Instruction instr, Type value)
            => instr.MatchSizeof(out var v) && v.Is(value.GetTypeInfo());
        public static bool MatchSizeof(this Instruction instr, TypeReference value)
            => instr.MatchSizeof(out var v) && v == value;
        public static bool MatchSizeof(this Instruction instr, out TypeReference value) {
            if (instr.OpCode == OpCodes.Sizeof) {
                value = (TypeReference) instr.Operand;
                return true;
            }
            value = default;
            return false;
        } // OperandType.InlineType

        public static bool MatchRefanytype(this Instruction instr) {
            if (instr.OpCode == OpCodes.Refanytype) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

        public static bool MatchReadonly(this Instruction instr) {
            if (instr.OpCode == OpCodes.Readonly) {
                return true;
            }
            return false;
        } // OperandType.InlineNone

    }
}
