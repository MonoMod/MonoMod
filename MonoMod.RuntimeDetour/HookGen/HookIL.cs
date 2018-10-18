using System;
using System.Reflection;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;
using System.Collections.ObjectModel;

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

        internal List<HookILLabel> _Labels = new List<HookILLabel>();
        public ReadOnlyCollection<HookILLabel> Labels => _Labels.AsReadOnly();

        internal bool _ReadOnly = false;

        public HookIL(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        public void Invoke(ILManipulator manip) {
            foreach (Instruction instr in Instrs) {
                if (instr.Operand is Instruction target)
                    instr.Operand = new HookILLabel(this, target);
            }

            manip(this);

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is HookILLabel label)
                    instr.Operand = label.Target;
            }
        }

        public bool MakeReadOnly()
            => _ReadOnly = true;

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

        public HookILCursor this[HookILLabel label] {
            get {
                return new HookILCursor(this, label.Target);
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

        public HookILLabel DefineLabel()
            => new HookILLabel(this);
        public HookILLabel DefineLabel(Instruction target)
            => new HookILLabel(this, target);

        public void ReplaceOperands(object from, object to) {
            foreach (Instruction instr in Instrs)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
        }

        public IEnumerable<HookILLabel> GetIncomingLabels(Instruction instr)
            => _Labels.Where(l => l.Target == instr);

    }
}
