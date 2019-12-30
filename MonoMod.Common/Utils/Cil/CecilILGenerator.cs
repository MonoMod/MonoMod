using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using Mono.Cecil.Cil;
using MCC = Mono.Cecil.Cil;
using SRE = System.Reflection.Emit;
using Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using OpCode = Mono.Cecil.Cil.OpCode;
using Mono.Collections.Generic;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;

namespace MonoMod.Utils.Cil {
    /// <summary>
    /// A variant of ILGenerator which uses Mono.Cecil under the hood.
    /// </summary>
    public sealed class CecilILGenerator : ILGeneratorShim {

        // https://github.com/Unity-Technologies/mono/blob/unity-5.6/mcs/class/corlib/System.Reflection.Emit/LocalBuilder.cs
        // https://github.com/Unity-Technologies/mono/blob/unity-2018.3-mbe/mcs/class/corlib/System.Reflection.Emit/LocalBuilder.cs
        // https://github.com/dotnet/coreclr/blob/master/src/System.Private.CoreLib/src/System/Reflection/Emit/LocalBuilder.cs
        // Mono: Type, ILGenerator
        // .NET Framework matches .NET Core: int, Type, MethodInfo(, bool)
        private static readonly ConstructorInfo c_LocalBuilder =
            typeof(LocalBuilder).GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)[0];
        private static ParameterInfo[] c_LocalBuilder_params = c_LocalBuilder.GetParameters();

        private static readonly Dictionary<short, OpCode> _MCCOpCodes = new Dictionary<short, OpCode>();

        static CecilILGenerator() {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                OpCode cecilOpCode = (OpCode) field.GetValue(null);
                _MCCOpCodes[cecilOpCode.Value] = cecilOpCode;
            }
        }

        /// <summary>
        /// The underlying Mono.Cecil.Cil.ILProcessor.
        /// </summary>
        public readonly ILProcessor IL;

        private readonly List<Instruction> _Labels = new List<Instruction>();
        private readonly Dictionary<LocalBuilder, VariableDefinition> _Variables = new Dictionary<LocalBuilder, VariableDefinition>();
        private readonly Stack<ExceptionHandlerChain> _ExceptionHandlers = new Stack<ExceptionHandlerChain>();

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public CecilILGenerator(ILProcessor il) {
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
            IL = il;
        }

        private OpCode _(SRE.OpCode opcode) => _MCCOpCodes[opcode.Value];
        private unsafe Instruction _(Label handle) => _Labels[*(int*) &handle];
        private VariableDefinition _(LocalBuilder handle) => _Variables[handle];

        private TypeReference _(Type info) => IL.Body.Method.Module.ImportReference(info);
        private FieldReference _(FieldInfo info) => IL.Body.Method.Module.ImportReference(info);
        private MethodReference _(MethodBase info) => IL.Body.Method.Module.ImportReference(info);

        public override int ILOffset => throw new NotSupportedException();

        public override unsafe Label DefineLabel() {
            Label handle = default;
            // The label struct holds a single int field on .NET Framework, .NET Core and Mono.
            *(int*) &handle = _Labels.Count;
            Instruction instr = IL.Create(OpCodes.Nop);
            _Labels.Add(instr);
            return handle;
        }

        public override void MarkLabel(Label loc) => MarkLabel(_(loc));
        private Instruction MarkLabel() => MarkLabel(_(DefineLabel()));
        private Instruction MarkLabel(Instruction instr) {
            Collection<Instruction> instrs = IL.Body.Instructions;
            int index = instrs.IndexOf(instr);
            if (index != -1)
                instrs.RemoveAt(index);
            IL.Append(instr);
            return instr;
        }


        public override LocalBuilder DeclareLocal(Type type) => DeclareLocal(type, false);
        public override LocalBuilder DeclareLocal(Type type, bool pinned) {
            // The handle itself is out of sync with the "backing" VariableDefinition.
            LocalBuilder handle = (LocalBuilder) (
                c_LocalBuilder_params.Length == 4 ? c_LocalBuilder.Invoke(new object[] { 0, type, null, false }) :
                c_LocalBuilder_params.Length == 3 ? c_LocalBuilder.Invoke(new object[] { 0, type, null }) :
                c_LocalBuilder_params.Length == 2 ? c_LocalBuilder.Invoke(new object[] { type, null }) :
                c_LocalBuilder_params.Length == 0 ? c_LocalBuilder.Invoke(new object[] { }) :
                throw new NotSupportedException()
            );

            TypeReference typeRef = _(type);
            if (pinned)
                typeRef = new PinnedType(typeRef);
            VariableDefinition def = new VariableDefinition(typeRef);
            IL.Body.Variables.Add(def);
            _Variables[handle] = def;

            return handle;
        }

        public override void Emit(SRE.OpCode opcode) => IL.Emit(_(opcode));
        public override void Emit(SRE.OpCode opcode, byte arg) {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(_(opcode), arg);
            else
                IL.Emit(_(opcode), arg);
        }
        public override void Emit(SRE.OpCode opcode, sbyte arg) {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(_(opcode), arg);
            else
                IL.Emit(_(opcode), arg);
        }
        public override void Emit(SRE.OpCode opcode, short arg) {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(_(opcode), arg);
            else
                IL.Emit(_(opcode), arg);
        }
        public override void Emit(SRE.OpCode opcode, int arg) {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(_(opcode), arg);
            else if (opcode.Name.EndsWith(".s"))
                IL.Emit(_(opcode), (sbyte) arg);
            else
                IL.Emit(_(opcode), arg);
        }
        public override void Emit(SRE.OpCode opcode, long arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, float arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, double arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, string arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, Type arg) => IL.Emit(_(opcode), _(arg));
        public override void Emit(SRE.OpCode opcode, FieldInfo arg) => IL.Emit(_(opcode), _(arg));
        public override void Emit(SRE.OpCode opcode, ConstructorInfo arg) => IL.Emit(_(opcode), _(arg));
        public override void Emit(SRE.OpCode opcode, MethodInfo arg) => IL.Emit(_(opcode), _(arg));
        public override void Emit(SRE.OpCode opcode, Label label) => IL.Emit(_(opcode), _(label));
        public override void Emit(SRE.OpCode opcode, Label[] labels) => IL.Emit(_(opcode), labels.Select(label => _(label)).ToArray());
        public override void Emit(SRE.OpCode opcode, LocalBuilder local) => IL.Emit(_(opcode), _(local));
        public override void Emit(SRE.OpCode opcode, SignatureHelper signature) => throw new NotSupportedException();

        private void _EmitInlineVar(OpCode opcode, int index) {
            // System.Reflection.Emit has only got (Short)InlineVar and allows index refs.
            // Mono.Cecil has also got (Short)InlineArg and requires definition refs.
            switch (opcode.OperandType) {
                case MCC.OperandType.ShortInlineArg:
                case MCC.OperandType.InlineArg:
                    IL.Emit(opcode, IL.Body.Method.Parameters[index]);
                    break;

                case MCC.OperandType.ShortInlineVar:
                case MCC.OperandType.InlineVar:
                    IL.Emit(opcode, IL.Body.Variables[index]);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported SRE InlineVar -> Cecil {opcode.OperandType} for {opcode} {index}");
            }
        }

        public override void EmitCall(SRE.OpCode opcode, MethodInfo methodInfo, Type[] optionalParameterTypes) => IL.Emit(_(opcode), _(methodInfo));
        public override void EmitCalli(SRE.OpCode opcode, CallingConventions callingConvention, Type returnType, Type[] parameterTypes, Type[] optionalParameterTypes) => throw new NotSupportedException();
        public override void EmitCalli(SRE.OpCode opcode, CallingConvention unmanagedCallConv, Type returnType, Type[] parameterTypes) => throw new NotSupportedException();

        public override void EmitWriteLine(FieldInfo field) {
            if (field.IsStatic)
                IL.Emit(OpCodes.Ldsfld, _(field));
            else {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _(field));
            }
            IL.Emit(OpCodes.Call, _(typeof(Console).GetMethod("WriteLine", new Type[1] { field.FieldType })));
        }

        public override void EmitWriteLine(LocalBuilder localBuilder) {
            IL.Emit(OpCodes.Ldloc, _(localBuilder));
            IL.Emit(OpCodes.Call, _(typeof(Console).GetMethod("WriteLine", new Type[1] { localBuilder.LocalType })));
        }

        public override void EmitWriteLine(string value) {
            IL.Emit(OpCodes.Ldstr, value);
            IL.Emit(OpCodes.Call, _(typeof(Console).GetMethod("WriteLine", new Type[1] { typeof(string) })));
        }

        public override void ThrowException(Type type) {
            IL.Emit(OpCodes.Newobj, _(type.GetConstructor(Type.EmptyTypes)));
            IL.Emit(OpCodes.Throw);
        }

        public override Label BeginExceptionBlock() {
            ExceptionHandlerChain chain = new ExceptionHandlerChain(this);
            _ExceptionHandlers.Push(chain);
            return chain.SkipAll;
        }

        public override void BeginCatchBlock(Type exceptionType) {
            ExceptionHandler handler = _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Catch);
            handler.CatchType = exceptionType == null ? null : _(exceptionType);
        }

        public override void BeginExceptFilterBlock() {
            _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Filter);
        }

        public override void BeginFaultBlock() {
            _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Fault);
        }

        public override void BeginFinallyBlock() {
            _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Finally);
        }

        public override void EndExceptionBlock() {
            _ExceptionHandlers.Pop().End();
        }

        public override void BeginScope() {
        }

        public override void EndScope() {
        }

        public override void UsingNamespace(string usingNamespace) {
        }

        private class ExceptionHandlerChain {

            private readonly CecilILGenerator IL;

            private readonly Instruction _Start;
            public readonly Label SkipAll;
            private readonly Instruction _SkipAllI;
            private Label _SkipHandler;

            private ExceptionHandler _Prev;
            private ExceptionHandler _Handler;

            public ExceptionHandlerChain(CecilILGenerator il) {
                IL = il;
                _Start = il.MarkLabel();
                SkipAll = il.DefineLabel();
                _SkipAllI = il._(SkipAll);
            }

            public ExceptionHandler BeginHandler(ExceptionHandlerType type) {
                ExceptionHandler prev = _Prev = _Handler;
                if (prev != null)
                    EndHandler(prev);

                IL.Emit(SRE.OpCodes.Leave, _SkipHandler = IL.DefineLabel());

                ExceptionHandler next = _Handler = new ExceptionHandler(0);
                Instruction firstHandlerInstr = IL.MarkLabel();
                next.TryStart = _Start;
                next.TryEnd = firstHandlerInstr;
                next.HandlerType = type;
                if (type == ExceptionHandlerType.Filter)
                    next.FilterStart = firstHandlerInstr;
                else
                    next.HandlerStart = firstHandlerInstr;

                IL.IL.Body.ExceptionHandlers.Add(next);
                return next;
            }

            public void EndHandler(ExceptionHandler handler) {
                Label skip = _SkipHandler;

                switch (handler.HandlerType) {
                    case ExceptionHandlerType.Filter:
                        IL.Emit(SRE.OpCodes.Endfilter);
                        break;

                    case ExceptionHandlerType.Finally:
                        IL.Emit(SRE.OpCodes.Endfinally);
                        break;

                    default:
                        IL.Emit(SRE.OpCodes.Leave, skip);
                        break;
                }

                IL.MarkLabel(skip);
                handler.HandlerEnd = IL._(skip);
            }

            public void End() {
                EndHandler(_Handler);
                IL.MarkLabel(SkipAll);
            }

        }

    }
}
