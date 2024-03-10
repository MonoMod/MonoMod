using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;
using MCC = Mono.Cecil.Cil;
using OpCode = Mono.Cecil.Cil.OpCode;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using SRE = System.Reflection.Emit;

namespace MonoMod.Utils.Cil
{
    /// <summary>
    /// A variant of ILGenerator which uses Mono.Cecil under the hood.
    /// </summary>
    public sealed class CecilILGenerator : ILGeneratorShim
    {
        // https://github.com/Unity-Technologies/mono/blob/unity-5.6/mcs/class/corlib/System.Reflection.Emit/LocalBuilder.cs
        // https://github.com/Unity-Technologies/mono/blob/unity-2018.3-mbe/mcs/class/corlib/System.Reflection.Emit/LocalBuilder.cs
        // https://github.com/dotnet/coreclr/blob/master/src/System.Private.CoreLib/src/System/Reflection/Emit/LocalBuilder.cs
        // Mono: Type, ILGenerator
        // .NET Framework matches .NET Core: int, Type, MethodInfo(, bool)
        private static readonly ConstructorInfo c_LocalBuilder =
            typeof(LocalBuilder).GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length).First();
        private static readonly FieldInfo? f_LocalBuilder_position =
            typeof(LocalBuilder).GetField("position", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? f_LocalBuilder_is_pinned =
            typeof(LocalBuilder).GetField("is_pinned", BindingFlags.NonPublic | BindingFlags.Instance);

        private static int c_LocalBuilder_params = c_LocalBuilder.GetParameters().Length;

        private static readonly Dictionary<short, OpCode> _MCCOpCodes = new Dictionary<short, OpCode>();

        private static Label NullLabel;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline",
            Justification = "The performance penalty for cctor checks is not worth caring about here. We already do some high-level shenanigans to get calls here.")]
        static CecilILGenerator()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var cecilOpCode = (OpCode)field.GetValue(null)!;
                _MCCOpCodes[cecilOpCode.Value] = cecilOpCode;
            }

            Label l = default;
            Unsafe.As<Label, int>(ref l) = -1;
            NullLabel = l;
        }

        /// <summary>
        /// The underlying Mono.Cecil.Cil.ILProcessor.
        /// </summary>
        public ILProcessor IL { get; }

        private readonly Dictionary<Label, LabelInfo> _LabelInfos = new Dictionary<Label, LabelInfo>();
        private readonly List<LabelInfo> _LabelsToMark = new List<LabelInfo>();
        private readonly List<LabelledExceptionHandler> _ExceptionHandlersToMark = new List<LabelledExceptionHandler>();

        private readonly Dictionary<LocalBuilder, VariableDefinition> _Variables =
            new Dictionary<LocalBuilder, VariableDefinition>();

        private readonly Stack<ExceptionHandlerChain> _ExceptionHandlers = new Stack<ExceptionHandlerChain>();

        private int labelCounter;

        public CecilILGenerator(ILProcessor il)
        {
            IL = il;
        }

        private static OpCode _(SRE.OpCode opcode) => _MCCOpCodes[opcode.Value];

        private LabelInfo? _(Label handle) =>
            _LabelInfos.TryGetValue(handle, out var labelInfo) ? labelInfo : null;

        private VariableDefinition _(LocalBuilder handle) => _Variables[handle];

        private TypeReference _(Type info) => IL.Body.Method.Module.ImportReference(info);
        private FieldReference _(FieldInfo info) => IL.Body.Method.Module.ImportReference(info);
        private MethodReference _(MethodBase info) => IL.Body.Method.Module.ImportReference(info);

        private int _ILOffset;
        public override int ILOffset => _ILOffset;

        private Instruction ProcessLabels(Instruction ins)
        {
            if (_LabelsToMark.Count != 0)
            {
                foreach (var labelInfo in _LabelsToMark)
                {
                    foreach (var insToFix in labelInfo.Branches)
                    {
                        switch (insToFix.Operand)
                        {
                            case Instruction:
                                insToFix.Operand = ins;
                                break;
                            case Instruction[] instrsOperand:
                                for (var i = 0; i < instrsOperand.Length; i++)
                                {
                                    if (instrsOperand[i] == labelInfo.Instruction)
                                    {
                                        instrsOperand[i] = ins;
                                        break;
                                    }
                                }
                                break;
                        }
                    }

                    labelInfo.Emitted = true;
                    labelInfo.Instruction = ins;
                }

                _LabelsToMark.Clear();
            }

            if (_ExceptionHandlersToMark.Count != 0)
            {
                foreach (var exHandler in _ExceptionHandlersToMark)
                    IL.Body.ExceptionHandlers.Add(new ExceptionHandler(exHandler.HandlerType)
                    {
                        TryStart = _(exHandler.TryStart)?.Instruction,
                        TryEnd = _(exHandler.TryEnd)?.Instruction,
                        HandlerStart = _(exHandler.HandlerStart)?.Instruction,
                        HandlerEnd = _(exHandler.HandlerEnd)?.Instruction,
                        FilterStart = _(exHandler.FilterStart)?.Instruction,
                        CatchType = exHandler.ExceptionType
                    });

                _ExceptionHandlersToMark.Clear();
            }

            return ins;
        }

        public override unsafe Label DefineLabel()
        {
            Label handle = default;
            // The label struct holds a single int field on .NET Framework, .NET Core and Mono.
            *(int*)&handle = labelCounter++;
            _LabelInfos[handle] = new LabelInfo();
            return handle;
        }

        public override void MarkLabel(Label loc)
        {
            if (!_LabelInfos.TryGetValue(loc, out var labelInfo) || labelInfo.Emitted)
                return;
            _LabelsToMark.Add(labelInfo);
        }

        public override LocalBuilder DeclareLocal(Type localType) => DeclareLocal(localType, false);

        public override LocalBuilder DeclareLocal(Type localType, bool pinned)
        {
            // The handle itself is out of sync with the "backing" VariableDefinition.
            var index = IL.Body.Variables.Count;
            var handle = (LocalBuilder)(
                c_LocalBuilder_params == 4 ? c_LocalBuilder.Invoke(new object?[] { index, localType, null, pinned }) :
                c_LocalBuilder_params == 3 ? c_LocalBuilder.Invoke(new object?[] { index, localType, null }) :
                c_LocalBuilder_params == 2 ? c_LocalBuilder.Invoke(new object?[] { localType, null }) :
                c_LocalBuilder_params == 0 ? c_LocalBuilder.Invoke(ArrayEx.Empty<object?>()) :
                throw new NotSupportedException()
            );

            f_LocalBuilder_position?.SetValue(handle, (ushort)index);
            f_LocalBuilder_is_pinned?.SetValue(handle, pinned);

            var typeRef = _(localType);
            if (pinned)
                typeRef = new PinnedType(typeRef);
            var def = new VariableDefinition(typeRef);
            IL.Body.Variables.Add(def);
            _Variables[handle] = def;

            return handle;
        }

        private void Emit(Instruction ins)
        {
            ins.Offset = _ILOffset;
            _ILOffset += ins.GetSize();
            IL.Append(ProcessLabels(ins));
        }

        public override void Emit(SRE.OpCode opcode) => Emit(IL.Create(CecilILGenerator._(opcode)));

        public override void Emit(SRE.OpCode opcode, byte arg)
        {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(CecilILGenerator._(opcode), arg);
            else
                Emit(IL.Create(CecilILGenerator._(opcode), arg));
        }

        public override void Emit(SRE.OpCode opcode, sbyte arg)
        {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(CecilILGenerator._(opcode), arg);
            else
                Emit(IL.Create(CecilILGenerator._(opcode), arg));
        }

        public override void Emit(SRE.OpCode opcode, short arg)
        {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(CecilILGenerator._(opcode), arg);
            else
                Emit(IL.Create(CecilILGenerator._(opcode), arg));
        }

        public override void Emit(SRE.OpCode opcode, int arg)
        {
            if (opcode.OperandType == SRE.OperandType.ShortInlineVar ||
                opcode.OperandType == SRE.OperandType.InlineVar)
                _EmitInlineVar(CecilILGenerator._(opcode), arg);
            else if (opcode.Name?.EndsWith(".s", StringComparison.Ordinal) ?? false)
                Emit(IL.Create(CecilILGenerator._(opcode), (sbyte)arg));
            else
                Emit(IL.Create(CecilILGenerator._(opcode), arg));
        }

        public override void Emit(SRE.OpCode opcode, long arg) => Emit(IL.Create(CecilILGenerator._(opcode), arg));
        public override void Emit(SRE.OpCode opcode, float arg) => Emit(IL.Create(CecilILGenerator._(opcode), arg));
        public override void Emit(SRE.OpCode opcode, double arg) => Emit(IL.Create(CecilILGenerator._(opcode), arg));
        public override void Emit(SRE.OpCode opcode, string str) => Emit(IL.Create(CecilILGenerator._(opcode), str));
        public override void Emit(SRE.OpCode opcode, Type cls) => Emit(IL.Create(CecilILGenerator._(opcode), _(cls)));
        public override void Emit(SRE.OpCode opcode, FieldInfo field) => Emit(IL.Create(CecilILGenerator._(opcode), _(field)));
        public override void Emit(SRE.OpCode opcode, ConstructorInfo con) => Emit(IL.Create(CecilILGenerator._(opcode), _(con)));
        public override void Emit(SRE.OpCode opcode, MethodInfo meth) => Emit(IL.Create(CecilILGenerator._(opcode), _(meth)));

        public override void Emit(SRE.OpCode opcode, Label label)
        {
            var info = _(label)!;
            var ins = IL.Create(CecilILGenerator._(opcode), _(label)!.Instruction);
            info.Branches.Add(ins);
            Emit(ProcessLabels(ins));
        }

        public override void Emit(SRE.OpCode opcode, Label[] labels)
        {
            var labelInfos = labels.Distinct().Select(_).Where(x => x is not null)!.ToArray();
            var ins = IL.Create(CecilILGenerator._(opcode), labelInfos.Select(labelInfo => labelInfo!.Instruction).ToArray());
            foreach (var labelInfo in labelInfos)
                labelInfo!.Branches.Add(ins);
            Emit(ProcessLabels(ins));
        }

        public override void Emit(SRE.OpCode opcode, LocalBuilder local) => Emit(IL.Create(CecilILGenerator._(opcode), _(local)));
        public override void Emit(SRE.OpCode opcode, SignatureHelper signature) => Emit(IL.Create(CecilILGenerator._(opcode), IL.Body.Method.Module.ImportCallSite(signature)));
        public void Emit(SRE.OpCode opcode, ICallSiteGenerator signature) => Emit(IL.Create(CecilILGenerator._(opcode), IL.Body.Method.Module.ImportCallSite(signature)));

        private void _EmitInlineVar(OpCode opcode, int index)
        {
            // System.Reflection.Emit has only got (Short)InlineVar and allows index refs.
            // Mono.Cecil has also got (Short)InlineArg and requires definition refs.
            switch (opcode.OperandType)
            {
                case MCC.OperandType.ShortInlineArg:
                case MCC.OperandType.InlineArg:
                    Emit(IL.Create(opcode, IL.Body.Method.Parameters[index]));
                    break;

                case MCC.OperandType.ShortInlineVar:
                case MCC.OperandType.InlineVar:
                    Emit(IL.Create(opcode, IL.Body.Variables[index]));
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported SRE InlineVar -> Cecil {opcode.OperandType} for {opcode} {index}");
            }
        }

        public override void EmitCall(SRE.OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) =>
            Emit(IL.Create(CecilILGenerator._(opcode), _(methodInfo)));

        public override void EmitCalli(SRE.OpCode opcode, CallingConventions callingConvention, Type? returnType,
            Type[]? parameterTypes, Type[]? optionalParameterTypes) => throw new NotSupportedException();

        public override void EmitCalli(SRE.OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType,
            Type[]? parameterTypes) => throw new NotSupportedException();

        public override void EmitWriteLine(FieldInfo fld)
        {
            if (fld.IsStatic)
                Emit(IL.Create(OpCodes.Ldsfld, _(fld)));
            else
            {
                Emit(IL.Create(OpCodes.Ldarg_0));
                Emit(IL.Create(OpCodes.Ldfld, _(fld)));
            }

            Emit(IL.Create(OpCodes.Call, _(typeof(Console).GetMethod("WriteLine", new[] { fld.FieldType })!)));
        }

        public override void EmitWriteLine(LocalBuilder localBuilder)
        {
            Emit(IL.Create(OpCodes.Ldloc, _(localBuilder)));
            Emit(IL.Create(OpCodes.Call,
                _(typeof(Console).GetMethod("WriteLine", new[] { localBuilder.LocalType })!)));
        }

        public override void EmitWriteLine(string value)
        {
            Emit(IL.Create(OpCodes.Ldstr, value));
            Emit(IL.Create(OpCodes.Call, _(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) })!)));
        }

        public override void ThrowException(Type excType)
        {
            Emit(IL.Create(OpCodes.Newobj, _(excType.GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("No default constructor"))));
            Emit(IL.Create(OpCodes.Throw));
        }

        public override Label BeginExceptionBlock()
        {
            var chain = new ExceptionHandlerChain(this);
            _ExceptionHandlers.Push(chain);
            return chain.SkipAll;
        }

        public override void BeginCatchBlock(Type exceptionType)
        {
            var handler = _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Catch);
            handler.ExceptionType = exceptionType is null ? null : _(exceptionType);
        }

        public override void BeginExceptFilterBlock()
        {
            _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Filter);
        }

        public override void BeginFaultBlock()
        {
            _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Fault);
        }

        public override void BeginFinallyBlock()
        {
            _ExceptionHandlers.Peek().BeginHandler(ExceptionHandlerType.Finally);
        }

        public override void EndExceptionBlock()
        {
            _ExceptionHandlers.Pop().End();
        }

        public override void BeginScope()
        {
        }

        public override void EndScope()
        {
        }

        public override void UsingNamespace(string usingNamespace)
        {
        }

        private class LabelInfo
        {
            public bool Emitted;
            public Instruction Instruction = Instruction.Create(OpCodes.Nop);
            public readonly List<Instruction> Branches = new List<Instruction>();
        }

        private class LabelledExceptionHandler
        {
            public Label TryStart = NullLabel;
            public Label TryEnd = NullLabel;
            public Label HandlerStart = NullLabel;
            public Label HandlerEnd = NullLabel;
            public Label FilterStart = NullLabel;
            public ExceptionHandlerType HandlerType;
            public TypeReference? ExceptionType;
        }

        private class ExceptionHandlerChain
        {
            private readonly CecilILGenerator IL;

            private readonly Label _Start;
            public readonly Label SkipAll;
            private Label _SkipHandler;

            private LabelledExceptionHandler? _Prev;
            private LabelledExceptionHandler? _Handler;

            public ExceptionHandlerChain(CecilILGenerator il)
            {
                IL = il;

                _Start = il.DefineLabel();
                il.MarkLabel(_Start);

                SkipAll = il.DefineLabel();
            }

            public LabelledExceptionHandler BeginHandler(ExceptionHandlerType type)
            {
                var prev = _Prev = _Handler;
                if (prev is not null)
                    EndHandler(prev);

                IL.Emit(SRE.OpCodes.Leave, _SkipHandler = IL.DefineLabel());

                var handlerStart = IL.DefineLabel();
                IL.MarkLabel(handlerStart);

                var next = _Handler = new LabelledExceptionHandler
                {
                    TryStart = _Start,
                    TryEnd = handlerStart,
                    HandlerType = type,
                    HandlerEnd = _SkipHandler
                };
                if (type == ExceptionHandlerType.Filter)
                    next.FilterStart = handlerStart;
                else
                    next.HandlerStart = handlerStart;

                return next;
            }

            public void EndHandler(LabelledExceptionHandler handler)
            {
                var skip = _SkipHandler;

                switch (handler.HandlerType)
                {
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
                IL._ExceptionHandlersToMark.Add(handler);
            }

            public void End()
            {
                EndHandler(_Handler ?? throw new InvalidOperationException("Cannot end when there is no current handler!"));
                IL.MarkLabel(SkipAll);
            }
        }
    }
}