using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;

namespace MonoMod.Utils
{
    public sealed partial class DynamicMethodDefinition
    {

        private static OpCode[] _CecilOpCodes1X = null!;
        private static OpCode[] _CecilOpCodes2X = null!;

        private static void _InitCopier()
        {
            _CecilOpCodes1X = new OpCode[0xe1];
            _CecilOpCodes2X = new OpCode[0x1f];

            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var opcode = (OpCode)field.GetValue(null)!;
                if (opcode.OpCodeType == OpCodeType.Nternal)
                    continue;

                if (opcode.Size == 1)
                    _CecilOpCodes1X[opcode.Value] = opcode;
                else
                    _CecilOpCodes2X[opcode.Value & 0xff] = opcode;
            }
        }

        private static void _CopyMethodToDefinition(MethodBase from, MethodDefinition into)
        {
            var moduleFrom = from.Module;
            var bodyFrom = from.GetMethodBody() ?? throw new NotSupportedException("Body-less method");
            var data = bodyFrom.GetILAsByteArray() ?? throw new InvalidOperationException();

            var moduleTo = into.Module;
            var bodyTo = into.Body;
            var processor = bodyTo.GetILProcessor();

            Type[]? typeArguments = null;
            if (from.DeclaringType?.IsGenericType ?? false)
                typeArguments = from.DeclaringType.GetGenericArguments();

            Type[]? methodArguments = null;
            if (from.IsGenericMethod)
                methodArguments = from.GetGenericArguments();

            foreach (var info in bodyFrom.LocalVariables)
            {
                var type = moduleTo.ImportReference(info.LocalType);
                if (info.IsPinned)
                    type = new PinnedType(type);
                bodyTo.Variables.Add(new VariableDefinition(type));
            }

            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                for (Instruction? instr = null, prev = null; reader.BaseStream.Position < reader.BaseStream.Length; prev = instr)
                {
                    var offset = (int)reader.BaseStream.Position;
                    instr = Instruction.Create(OpCodes.Nop);
                    var op = reader.ReadByte();
                    instr.OpCode = op != 0xfe ? _CecilOpCodes1X[op] : _CecilOpCodes2X[reader.ReadByte()];
                    instr.Offset = offset;
                    if (prev != null)
                        prev.Next = instr;
                    instr.Previous = prev;
                    ReadOperand(reader, instr);
                    bodyTo.Instructions.Add(instr);
                }
            }

            foreach (var instr in bodyTo.Instructions)
            {
                switch (instr.OpCode.OperandType)
                {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instr.Operand = GetInstruction((int)instr.Operand!);
                        break;

                    case OperandType.InlineSwitch:
                        var offsets = (int[])instr.Operand!;
                        var targets = new Instruction[offsets.Length];
                        for (var i = 0; i < offsets.Length; i++)
                            targets[i] = GetInstruction(offsets[i])!;
                        instr.Operand = targets;
                        break;
                }
            }

            foreach (var clause in bodyFrom.ExceptionHandlingClauses)
            {
                var handler = new ExceptionHandler((ExceptionHandlerType)clause.Flags);
                bodyTo.ExceptionHandlers.Add(handler);

                handler.TryStart = GetInstruction(clause.TryOffset);
                handler.TryEnd = GetInstruction(clause.TryOffset + clause.TryLength);

                handler.FilterStart = handler.HandlerType != ExceptionHandlerType.Filter ? null : GetInstruction(clause.FilterOffset);
                handler.HandlerStart = GetInstruction(clause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(clause.HandlerOffset + clause.HandlerLength);

                handler.CatchType = handler.HandlerType != ExceptionHandlerType.Catch ? null : clause.CatchType == null ? null : moduleTo.ImportReference(clause.CatchType);
            }

            void ReadOperand(BinaryReader reader, Instruction instr)
            {
                int index, offs, length;
                switch (instr.OpCode.OperandType)
                {
                    case OperandType.InlineNone:
                        instr.Operand = null;
                        break;

                    case OperandType.InlineSwitch:
                        length = reader.ReadInt32();
                        offs = (int)reader.BaseStream.Position + (4 * length);
                        var targets = new int[length];
                        for (var i = 0; i < length; i++)
                            targets[i] = reader.ReadInt32() + offs;
                        instr.Operand = targets;
                        break;

                    case OperandType.ShortInlineBrTarget:
                        offs = reader.ReadSByte();
                        instr.Operand = (int)reader.BaseStream.Position + offs;
                        break;

                    case OperandType.InlineBrTarget:
                        offs = reader.ReadInt32();
                        instr.Operand = (int)reader.BaseStream.Position + offs;
                        break;

                    case OperandType.ShortInlineI:
                        instr.Operand = instr.OpCode == OpCodes.Ldc_I4_S ? reader.ReadSByte() : (object)reader.ReadByte();
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
                        instr.Operand = moduleTo.ImportCallSite(moduleFrom, moduleFrom.ResolveSignature(reader.ReadInt32()));
                        break;

                    case OperandType.InlineString:
                        instr.Operand = moduleFrom.ResolveString(reader.ReadInt32());
                        break;

                    case OperandType.InlineTok:
                        instr.Operand = ResolveTokenAs(reader.ReadInt32(), TokenResolutionMode.Any);
                        break;

                    case OperandType.InlineType:
                        instr.Operand = ResolveTokenAs(reader.ReadInt32(), TokenResolutionMode.Type);
                        break;

                    case OperandType.InlineMethod:
                        instr.Operand = ResolveTokenAs(reader.ReadInt32(), TokenResolutionMode.Method);
                        break;

                    case OperandType.InlineField:
                        instr.Operand = ResolveTokenAs(reader.ReadInt32(), TokenResolutionMode.Field);
                        break;

                    case OperandType.ShortInlineVar:
                    case OperandType.InlineVar:
                        index = instr.OpCode.OperandType == OperandType.ShortInlineVar ? reader.ReadByte() : reader.ReadInt16();
                        instr.Operand = bodyTo.Variables[index];
                        break;

                    case OperandType.InlineArg:
                    case OperandType.ShortInlineArg:
                        index = instr.OpCode.OperandType == OperandType.ShortInlineArg ? reader.ReadByte() : reader.ReadInt16();
                        instr.Operand = into.Parameters[index];
                        break;

                    case OperandType.InlinePhi: // No opcode seems to use this
                    default:
                        throw new NotSupportedException($"Unsupported opcode ${instr.OpCode.Name}");
                }
            }

            MemberReference ResolveTokenAs(int token, TokenResolutionMode resolveMode)
            {
                try
                {
                    switch (resolveMode)
                    {
                        case TokenResolutionMode.Type:
                            var resolvedType = moduleFrom.ResolveType(token, typeArguments, methodArguments);
                            resolvedType.FixReflectionCacheAuto();
                            return moduleTo.ImportReference(resolvedType);

                        case TokenResolutionMode.Method:
                            var resolvedMethod = moduleFrom.ResolveMethod(token, typeArguments, methodArguments);
                            resolvedMethod?.GetRealDeclaringType()?.FixReflectionCacheAuto();
                            return moduleTo.ImportReference(resolvedMethod);

                        case TokenResolutionMode.Field:
                            var resolvedField = moduleFrom.ResolveField(token, typeArguments, methodArguments);
                            resolvedField?.GetRealDeclaringType()?.FixReflectionCacheAuto();
                            return moduleTo.ImportReference(resolvedField);

                        case TokenResolutionMode.Any:
                            switch (moduleFrom.ResolveMember(token, typeArguments, methodArguments))
                            {
                                case Type i:
                                    i.FixReflectionCacheAuto();
                                    return moduleTo.ImportReference(i);

                                case MethodBase i:
                                    i.GetRealDeclaringType()?.FixReflectionCacheAuto();
                                    return moduleTo.ImportReference(i);

                                case FieldInfo i:
                                    i.GetRealDeclaringType()?.FixReflectionCacheAuto();
                                    return moduleTo.ImportReference(i);

                                case var resolved:
                                    throw new NotSupportedException($"Invalid resolved member type {resolved?.GetType()}");
                            }

                        default:
                            throw new NotSupportedException($"Invalid TokenResolutionMode {resolveMode}");
                    }

                }
                catch (MissingMemberException)
                {
                    // we could not resolve the method normally, so lets read the import table
                    // but we can only do that if the module was loaded from disk
                    // this can still throw if the assembly is a dynamic one, but if that's broken, you have bigger issues
                    var filePath = moduleFrom.Assembly.Location;
                    if (!File.Exists(filePath))
                    {
                        // in this case, the fallback cannot be followed, and so throwing the original error gives the user information
                        throw;
                    }

                    // TODO: make this cached somehow so its not read and re-opened a bunch
                    using (var assembly = AssemblyDefinition.ReadAssembly(filePath, new ReaderParameters
                    {
                        ReadingMode = ReadingMode.Deferred
                    }))
                    {
                        var module = assembly.Modules.First(m => m.Name == moduleFrom.Name);
                        // this should only fail if the token itself is somehow wrong
                        var reference = (MemberReference)module.LookupToken(token);
                        // the explicit casts here are to throw if they are incorrect
                        // normally the references would need to be imported, but moduleTo isn't written to anywhere
                        switch (resolveMode)
                        {
                            case TokenResolutionMode.Type:
                                return (TypeReference)reference;

                            case TokenResolutionMode.Method:
                                return (MethodReference)reference;

                            case TokenResolutionMode.Field:
                                return (FieldReference)reference;

                            case TokenResolutionMode.Any:
                                return reference;

                            default:
                                throw new NotSupportedException($"Invalid TokenResolutionMode {resolveMode}");
                        }
                    }
                }
            }

            Instruction? GetInstruction(int offset)
            {
                var last = bodyTo.Instructions.Count - 1;
                if (offset < 0 || offset > bodyTo.Instructions[last].Offset)
                    return null;

                var min = 0;
                var max = last;
                while (min <= max)
                {
                    var mid = min + ((max - min) / 2);
                    var instr = bodyTo.Instructions[mid];

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

        private enum TokenResolutionMode
        {
            Any,
            Type,
            Method,
            Field
        }

    }
}
