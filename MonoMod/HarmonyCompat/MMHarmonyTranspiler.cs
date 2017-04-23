using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using StringInject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonoMod.NET40Shim;
using MonoMod.Helpers;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Collections;

namespace MonoMod.HarmonyCompat {
    public class MMHarmonyInstruction {
        public System.Reflection.Emit.OpCode opcode;
        public object operand;
        public List<Label> labels = new List<Label>();
    }
    public class MMHarmonyTranspiler : IEnumerator<MMHarmonyInstruction> {

        private readonly static IntDictionary<System.Reflection.Emit.OpCode> _ReflOpCodes = new IntDictionary<System.Reflection.Emit.OpCode>();
        private readonly static IntDictionary<Mono.Cecil.Cil.OpCode> _CecilOpCodes = new IntDictionary<Mono.Cecil.Cil.OpCode>();

        static MMHarmonyTranspiler() {
            FieldInfo[] reflOpCodes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            FieldInfo[] cecilOpCodes = typeof(Mono.Cecil.Cil.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in reflOpCodes) {
                System.Reflection.Emit.OpCode reflOpCode = (System.Reflection.Emit.OpCode) field.GetValue(null);
                _ReflOpCodes[reflOpCode.Value] = reflOpCode;
            }

            foreach (FieldInfo field in cecilOpCodes) {
                Mono.Cecil.Cil.OpCode cecilOpCode = (Mono.Cecil.Cil.OpCode) field.GetValue(null);
                _CecilOpCodes[cecilOpCode.Value] = cecilOpCode;
            }
        }

        public MMHarmonyInstance MMHarmony;
        public MethodDefinition Method;
        public MethodBase MethodTarget;

        public MMHarmonyInstruction Current { get; private set; }

        private int _Index = 0;

        private ILGenerator _IL;
        private List<MMHarmonyInstruction> _Instrs = new List<MMHarmonyInstruction>();
        private IntDictionary<Label> _Labels = new IntDictionary<Label>();
        private IntDictionary<LocalBuilder> _Locals = new IntDictionary<LocalBuilder>();

        public MMHarmonyTranspiler(MMHarmonyInstance mmHarmony, MethodDefinition method, MethodBase methodTarget) {
            MMHarmony = mmHarmony;
            Method = method;
            MethodTarget = methodTarget;
        }

        public void Init(ILGenerator il, MethodBase orig, IEnumerable instrs) {
            // A fixed local offset could grow based on how Harmony behaves.
            _IL = il;
        }

        public bool MoveNext() {
            MMHarmonyInstruction hinstr = new MMHarmonyInstruction();
            _Instrs.Add(hinstr);
            _ApplyLabels(_Index);
            Instruction instr = Method.Body.Instructions[_Index];

            // MMHarmony.Log($"[Transpiler] {Method}: {_Index}: Input: {instr.OpCode} {instr.Operand}");

            hinstr.opcode = _ReflOpCodes[instr.OpCode.Value];
            hinstr.operand = instr.Operand;

            if (instr.Operand is Instruction)
                hinstr.operand = _GetLabel(Method.Body.Instructions.IndexOf((Instruction) instr.Operand));
            else if (instr.Operand is VariableDefinition)
                hinstr.operand = ((VariableDefinition) instr.Operand).Index;
            else if (instr.Operand is MemberReference) {
                MemberReference mref = (MemberReference) instr.Operand;
                MemberReference mdef = (MemberReference) mref.Resolve();
                // FIXME: What about TypeSpecifications?
                hinstr.operand =
                    Assembly.Load(mdef.Module.Assembly.Name.FullName)
                    .GetModule(mdef.Module.Name)
                    .ResolveMember(mdef.MetadataToken.ToInt32());
                
            }
                

            if (Code.Ldloc_0 <= instr.OpCode.Code && instr.OpCode.Code <= Code.Ldloc_3) {
                int index = instr.OpCode.Code - Code.Ldloc_0;
                LocalBuilder local = _GetLocal(index);
                hinstr.opcode = System.Reflection.Emit.OpCodes.Ldloc;
                hinstr.operand = local;
            } else if (Code.Stloc_0 <= instr.OpCode.Code && instr.OpCode.Code <= Code.Stloc_3) {
                int index = instr.OpCode.Code - Code.Stloc_0;
                LocalBuilder local = _GetLocal(index);
                hinstr.opcode = System.Reflection.Emit.OpCodes.Stloc;
                hinstr.operand = local;
            } else if (instr.OpCode.OperandType == Mono.Cecil.Cil.OperandType.InlineVar || instr.OpCode.OperandType == Mono.Cecil.Cil.OperandType.ShortInlineVar) {
                int index = (int) hinstr.operand;
                LocalBuilder local = _GetLocal(index);
                hinstr.operand = local;
                if (instr.OpCode.OperandType == Mono.Cecil.Cil.OperandType.ShortInlineVar) {
                    if (hinstr.opcode == System.Reflection.Emit.OpCodes.Ldloc_S)
                        hinstr.opcode = System.Reflection.Emit.OpCodes.Ldloc;
                    else if (hinstr.opcode == System.Reflection.Emit.OpCodes.Ldloca_S)
                        hinstr.opcode = System.Reflection.Emit.OpCodes.Ldloca;
                    else if (hinstr.opcode == System.Reflection.Emit.OpCodes.Stloc_S)
                        hinstr.opcode = System.Reflection.Emit.OpCodes.Stloc;
                }
            }

            // MMHarmony.Log($"[Transpiler] {Method}: {_Index}: Output: {hinstr.opcode} {hinstr.opcode}");
            Current = hinstr;
            return ++_Index < Method.Body.Instructions.Count;
        }

        private LocalBuilder _GetLocal(int index) {
            LocalBuilder local;
            if (_Locals.TryGetValue(index, out local))
                return local;

            TypeReference tref = Method.Body.Variables[index].VariableType.Resolve();
            local = _IL.DeclareLocal(
                Assembly.Load(tref.Module.Assembly.Name.FullName)
                .GetModule(tref.Module.Name)
                .ResolveType(tref.MetadataToken.ToInt32())
            );
            _Locals[index] = local;
            return local;
        }

        private Label _GetLabel(int index) {
            Label label;
            if (_Labels.TryGetValue(index, out label))
                return label;

            label = _IL.DefineLabel();
            _Labels[index] = label;
            _ApplyLabels(index);
            return label;
        }

        private void _ApplyLabels(int index) {
            Label label;
            if (!_Labels.TryGetValue(index, out label))
                return;

            if (index < _Instrs.Count)
                _Instrs[index].labels.Add(label);
        }

        object IEnumerator.Current {
            get {
                return Current;
            }
        }

        public void Reset() {
        }

        public void Dispose() {
        }

    }
}
