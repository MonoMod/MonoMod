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

        public MMHarmonyTranspiler(MMHarmonyInstance mmHarmony, MethodDefinition method, MethodBase methodTarget) {
            MMHarmony = mmHarmony;
            Method = method;
            MethodTarget = methodTarget;
        }

        public bool MoveNext() {
            MMHarmonyInstruction hinstr = new MMHarmonyInstruction();
            Instruction instr = Method.Body.Instructions[_Index];

            hinstr.opcode = _ReflOpCodes[instr.OpCode.Value];

            if (instr.Operand is Instruction)
                hinstr.operand = ((Instruction) instr.Operand).Offset;
            else if (instr.Operand is MemberReference)
                hinstr.operand =
                    Assembly.Load(((MemberReference) instr.Operand).Module.Assembly.Name.FullName)
                    .GetModule(((MemberReference) instr.Operand).Module.Name)
                    .ResolveMember(((MemberReference) instr.Operand).MetadataToken.ToInt32());
            else
                hinstr.operand = instr.Operand;

            return ++_Index < Method.Body.Instructions.Count;
        }

        object IEnumerator.Current {
            get {
                return Current;
            }
        }

        public void Dispose() {
            throw new InvalidOperationException();
        }

        public void Reset() {
            throw new InvalidOperationException();
        }

    }
}
