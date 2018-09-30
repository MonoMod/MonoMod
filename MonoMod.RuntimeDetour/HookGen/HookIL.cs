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

namespace MonoMod.RuntimeDetour.HookGen {
    /// <summary>
    /// Wrapper class used by the ILManipulator in HookExtensions.
    /// </summary>
    public class HookIL {

        public MethodDefinition Method { get; private set; }
        public ILProcessor IL { get; private set; }

        public MethodBody Body => Method.Body;
        public ModuleDefinition Module => Method.Module;
        public Mono.Collections.Generic.Collection<Instruction> Instrs => Body.Instructions;

        public List<HookILLabel> Labels { get; } = new List<HookILLabel>();

        public HookIL(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        public void Invoke(ILManipulator manip) {
            manip(this);
            foreach (HookILLabel label in Labels)
                IL.ReplaceOperands(label, label.Instr);
        }

        public Instruction this[int index] {
            get {
                if (index == -1 || index == Instrs.Count)
                    return null;
                return Instrs[index];
            }
        }

        public HookILCursor this[Instruction instr] {
            get {
                return new HookILCursor(this, instr);
            }
        }

        public HookILCursor At(int index)
            => this[this[index]];

        public FieldReference Import(FieldInfo field)
            => Module.ImportReference(field);
        public MethodReference Import(MethodBase method)
            => Module.ImportReference(method);
        public TypeReference Import(Type type)
            => Module.ImportReference(type);

        public HookILLabel DefineLabel() {
            HookILLabel label = new HookILLabel();
            Labels.Add(label);
            return label;
        }

    }
}
