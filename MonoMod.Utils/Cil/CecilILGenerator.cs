using MonoMod.Cil;
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

namespace MonoMod.Utils.Cil {
    public sealed class CecilILGenerator : ILGeneratorShim {

        // https://github.com/Unity-Technologies/mono/blob/unity-5.6/mcs/class/corlib/System.Reflection.Emit/LocalBuilder.cs
        // https://github.com/Unity-Technologies/mono/blob/unity-2018.3-mbe/mcs/class/corlib/System.Reflection.Emit/LocalBuilder.cs
        private static readonly ConstructorInfo c_LocalBuilder_mono =
            typeof(LocalBuilder).GetConstructor(new Type[] { typeof(Type), typeof(ILGenerator) });

        // https://github.com/dotnet/coreclr/blob/master/src/System.Private.CoreLib/src/System/Reflection/Emit/LocalBuilder.cs
        // .NET Framework matches .NET Core
        private static readonly ConstructorInfo c_LocalBuilder_corefx =
            typeof(LocalBuilder).GetConstructor(new Type[] { typeof(int), typeof(Type), typeof(MethodInfo), typeof(bool) });

        private static readonly Dictionary<short, OpCode> _MCCOpCodes = new Dictionary<short, OpCode>();

        static CecilILGenerator() {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                OpCode cecilOpCode = (OpCode) field.GetValue(null);
                _MCCOpCodes[cecilOpCode.Value] = cecilOpCode;
            }
        }

        public readonly ILProcessor IL;

        private readonly List<Instruction> _Labels = new List<Instruction>();
        private readonly Dictionary<LocalBuilder, VariableDefinition> _Variables = new Dictionary<LocalBuilder, VariableDefinition>();

        public CecilILGenerator(ILProcessor il) {
            IL = il;
        }

        private OpCode _(SRE.OpCode opcode) => _MCCOpCodes[opcode.Value];
        private unsafe Instruction _(Label handle) => _Labels[*(int*) &handle];
        private VariableDefinition _(LocalBuilder handle) => _Variables[handle];

        public override int ILOffset {
            get {
                throw new NotSupportedException();
            }
        }

        public override unsafe Label DefineLabel() {
            Label handle = Activator.CreateInstance<Label>();
            // The label struct holds a single int field on .NET Framework, .NET Core and Mono.
            *(int*) &handle = _Labels.Count;
            Instruction instr = IL.Create(OpCodes.Nop);
            _Labels.Add(instr);
            return handle;
        }

        public override void MarkLabel(Label loc) {
            Collection<Instruction> instrs = IL.Body.Instructions;
            Instruction instr = _(loc);
            int index = instrs.IndexOf(instr);
            if (index != -1)
                instrs.RemoveAt(index);
            IL.Append(instr);
        }

        public override LocalBuilder DeclareLocal(Type type) => DeclareLocal(type, false);
        public override LocalBuilder DeclareLocal(Type type, bool pinned) {
            // The handle itself is out of sync with the "backing" VariableDefinition.
            LocalBuilder handle = (LocalBuilder) (
                c_LocalBuilder_mono?.Invoke(new object[] { type, null }) ??
                c_LocalBuilder_corefx?.Invoke(new object[] { 0, type, null, false })
            );

            TypeReference typeRef = IL.Import(type);
            if (pinned)
                typeRef = new PinnedType(typeRef);
            VariableDefinition def = new VariableDefinition(typeRef);
            IL.Body.Variables.Add(def);
            _Variables[handle] = def;

            return handle;
        }

        public override void Emit(SRE.OpCode opcode) => IL.Emit(_(opcode));
        public override void Emit(SRE.OpCode opcode, byte arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, sbyte arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, short arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, int arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, long arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, float arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, double arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, string arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, Type arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, FieldInfo arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, ConstructorInfo arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, MethodInfo arg) => IL.Emit(_(opcode), arg);
        public override void Emit(SRE.OpCode opcode, Label label) => IL.Emit(_(opcode), _(label));
        public override void Emit(SRE.OpCode opcode, Label[] labels) => IL.Emit(_(opcode), labels.Select(label => _(label)).ToArray());
        public override void Emit(SRE.OpCode opcode, LocalBuilder local) => IL.Emit(_(opcode), _(local));
        public override void Emit(SRE.OpCode opcode, SignatureHelper signature) => throw new NotSupportedException();

        public override void EmitCall(SRE.OpCode opcode, MethodInfo methodInfo, Type[] optionalParameterTypes) => IL.Emit(_(opcode), methodInfo);
        public override void EmitCalli(SRE.OpCode opcode, CallingConventions callingConvention, Type returnType, Type[] parameterTypes, Type[] optionalParameterTypes) => throw new NotSupportedException();
        public override void EmitCalli(SRE.OpCode opcode, CallingConvention unmanagedCallConv, Type returnType, Type[] parameterTypes) => throw new NotSupportedException();

        public override void EmitWriteLine(FieldInfo field) {
            if (field.IsStatic)
                IL.Emit(OpCodes.Ldsfld, field);
            else {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, field);
            }
            IL.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new Type[1] { field.FieldType }));
        }

        public override void EmitWriteLine(LocalBuilder localBuilder) {
            IL.Emit(OpCodes.Ldloc, _(localBuilder));
            IL.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new Type[1] { localBuilder.LocalType }));
        }

        public override void EmitWriteLine(string value) {
            IL.Emit(OpCodes.Ldstr, value);
            IL.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new Type[1] { typeof(string) }));
        }

        public override void ThrowException(Type type) {
            IL.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes));
            IL.Emit(OpCodes.Throw);
        }

        public override Label BeginExceptionBlock() {
            throw new NotImplementedException();
        }

        public override void BeginCatchBlock(Type exceptionType) {
            throw new NotImplementedException();
        }

        public override void BeginExceptFilterBlock() {
            throw new NotImplementedException();
        }

        public override void BeginFaultBlock() {
            throw new NotImplementedException();
        }

        public override void BeginFinallyBlock() {
            throw new NotImplementedException();
        }

        public override void EndExceptionBlock() {
            throw new NotImplementedException();
        }

        public override void BeginScope() {
            // Does nothing in Mono.
        }

        public override void EndScope() {
            // Does nothing in Mono.
        }

        public override void UsingNamespace(string usingNamespace) {
            // Does nothing in Mono.
        }

    }
}
