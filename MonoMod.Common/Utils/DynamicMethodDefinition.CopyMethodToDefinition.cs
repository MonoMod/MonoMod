using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Security;
using System.IO;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;

namespace MonoMod.Utils {
#if !MONOMOD_INTERNAL
    public
#endif
    sealed partial class DynamicMethodDefinition {

        private static OpCode[] _CecilOpCodes1X;
        private static OpCode[] _CecilOpCodes2X;

        private static void _InitCopier() {
            _CecilOpCodes1X = new OpCode[0xe1];
            _CecilOpCodes2X = new OpCode[0x1f];

            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                OpCode opcode = (OpCode) field.GetValue(null);
                if (opcode.OpCodeType == OpCodeType.Nternal)
                    continue;

                if (opcode.Size == 1)
                    _CecilOpCodes1X[opcode.Value] = opcode;
                else
                    _CecilOpCodes2X[opcode.Value & 0xff] = opcode;
            }
        }

        private void _CopyMethodToDefinition() {
            MethodBase method = OriginalMethod;
            Module moduleFrom = method.Module;
            System.Reflection.MethodBody bodyFrom = method.GetMethodBody();
            byte[] data = bodyFrom?.GetILAsByteArray();
            if (data == null)
                throw new NotSupportedException("Body-less method");

            MethodDefinition def = Definition;
            ModuleDefinition moduleTo = def.Module;
            Mono.Cecil.Cil.MethodBody bodyTo = def.Body;
            ILProcessor processor = bodyTo.GetILProcessor();

            Type[] typeArguments = null;
            if (method.DeclaringType.IsGenericType)
                typeArguments = method.DeclaringType.GetGenericArguments();

            Type[] methodArguments = null;
            if (method.IsGenericMethod)
                methodArguments = method.GetGenericArguments();

            foreach (LocalVariableInfo info in bodyFrom.LocalVariables) {
                TypeReference type = moduleTo.ImportReference(info.LocalType);
                if (info.IsPinned)
                    type = new PinnedType(type);
                bodyTo.Variables.Add(new VariableDefinition(type));
            }

            using (BinaryReader reader = new BinaryReader(new MemoryStream(data))) {
                while (reader.BaseStream.Position < reader.BaseStream.Length) {
                    int offset = (int) reader.BaseStream.Position;
                    Instruction instr = Instruction.Create(OpCodes.Nop);
                    byte op = reader.ReadByte();
                    instr.OpCode = op != 0xfe ? _CecilOpCodes1X[op] : _CecilOpCodes2X[reader.ReadByte()];
                    instr.Offset = offset;
                    ReadOperand(reader, instr);
                    bodyTo.Instructions.Add(instr);
                }
            }

            foreach (Instruction instr in bodyTo.Instructions) {
                switch (instr.OpCode.OperandType) {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instr.Operand = GetInstruction((int) instr.Operand);
                        break;

                    case OperandType.InlineSwitch:
                        int[] offsets = (int[]) instr.Operand;
                        Instruction[] targets = new Instruction[offsets.Length];
                        for (int i = 0; i < offsets.Length; i++)
                            targets[i] = GetInstruction(offsets[i]);
                        instr.Operand = targets;
                        break;
                }
            }

            foreach (ExceptionHandlingClause clause in bodyFrom.ExceptionHandlingClauses) {
                ExceptionHandler handler = new ExceptionHandler((ExceptionHandlerType) clause.Flags);
                bodyTo.ExceptionHandlers.Add(handler);

                handler.TryStart = GetInstruction(clause.TryOffset);
                handler.TryEnd = GetInstruction(clause.TryOffset + clause.TryLength);

                handler.FilterStart = handler.HandlerType != ExceptionHandlerType.Filter ? null : GetInstruction(clause.FilterOffset);
                handler.HandlerStart = GetInstruction(clause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(clause.HandlerOffset + clause.HandlerLength);

                handler.CatchType = handler.HandlerType != ExceptionHandlerType.Catch ? null : clause.CatchType == null ? null : moduleTo.ImportReference(clause.CatchType);
            }

            void ReadOperand(BinaryReader reader, Instruction instr) {
                int index, offs, length;
                switch (instr.OpCode.OperandType) {
                    case OperandType.InlineNone:
                        instr.Operand = null;
                        break;

                    case OperandType.InlineSwitch:
                        length = reader.ReadInt32();
                        offs = (int) reader.BaseStream.Position + (4 * length);
                        int[] targets = new int[length];
                        for (int i = 0; i < length; i++)
                            targets[i] = reader.ReadInt32() + offs;
                        instr.Operand = targets;
                        break;

                    case OperandType.ShortInlineBrTarget:
                        offs = reader.ReadSByte();
                        instr.Operand = (int) reader.BaseStream.Position + offs;
                        break;

                    case OperandType.InlineBrTarget:
                        offs = reader.ReadInt32();
                        instr.Operand = (int) reader.BaseStream.Position + offs;
                        break;

                    case OperandType.ShortInlineI:
                        instr.Operand = instr.OpCode == OpCodes.Ldc_I4_S ? reader.ReadSByte() : (object) reader.ReadByte();
                        break;

                    case OperandType.InlineI:
                        instr.Operand = reader.ReadInt32();
                        break;

                    case OperandType.ShortInlineR:
                        instr.Operand = reader.ReadSingle();
                        break;

                    case OperandType.InlineR:
                        instr.Operand = reader.ReadDouble();
                        break;

                    case OperandType.InlineI8:
                        instr.Operand = reader.ReadInt64();
                        break;

                    case OperandType.InlineSig:
                        throw new NotSupportedException("Parsing CallSites at runtime currently not supported");

                    case OperandType.InlineString:
                        instr.Operand = moduleFrom.ResolveString(reader.ReadInt32());
                        break;

                    case OperandType.InlineTok:
                        switch (moduleFrom.ResolveMember(reader.ReadInt32(), typeArguments, methodArguments)) {
                            case Type i:
                                instr.Operand = moduleTo.ImportReference(i);
                                break;

                            case FieldInfo i:
                                instr.Operand = moduleTo.ImportReference(i);
                                break;

                            case MethodBase i:
                                instr.Operand = moduleTo.ImportReference(i);
                                break;
                        }
                        break;

                    case OperandType.InlineType:
                        instr.Operand = moduleTo.ImportReference(moduleFrom.ResolveType(reader.ReadInt32(), typeArguments, methodArguments));
                        break;

                    case OperandType.InlineMethod:
                        instr.Operand = moduleTo.ImportReference(moduleFrom.ResolveMethod(reader.ReadInt32(), typeArguments, methodArguments));
                        break;

                    case OperandType.InlineField:
                        instr.Operand = moduleTo.ImportReference(moduleFrom.ResolveField(reader.ReadInt32(), typeArguments, methodArguments));
                        break;

                    case OperandType.ShortInlineVar:
                    case OperandType.InlineVar:
                        index = instr.OpCode.OperandType == OperandType.ShortInlineVar ? reader.ReadByte() : reader.ReadInt16();
                        instr.Operand = bodyTo.Variables[index];
                        break;

                    case OperandType.InlineArg:
                    case OperandType.ShortInlineArg:
                        index = instr.OpCode.OperandType == OperandType.ShortInlineArg ? reader.ReadByte() : reader.ReadInt16();
                        instr.Operand = def.Parameters[index];
                        break;

                    case OperandType.InlinePhi: // No opcode seems to use this
                    default:
                        throw new NotSupportedException($"Unsupported opcode ${instr.OpCode.Name}");
                }
            }

            Instruction GetInstruction(int offset) {
                int last = bodyTo.Instructions.Count - 1;
                if (offset < 0 || offset > bodyTo.Instructions[last].Offset)
                    return null;

                int min = 0;
                int max = last;
                while (min <= max) {
                    int mid = min + ((max - min) / 2);
                    Instruction instr = bodyTo.Instructions[mid];

                    if (offset == instr.Offset)
                        return instr;

                    if (offset < instr.Offset)
                        max = mid - 1;
                    else
                        min = mid + 1;
                }

                return null;
            }

        }

    }
}
