using System;
using System.Reflection;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;
using System.Collections.ObjectModel;

namespace MonoMod.Utils {
    public class CecIL : IDisposable {
        public delegate void Manipulator(CecIL il);

        public MethodDefinition Method { get; private set; }
        public ILProcessor IL { get; private set; }

        public MethodBody Body => Method.Body;
        public ModuleDefinition Module => Method.Module;
        public Mono.Collections.Generic.Collection<Instruction> Instrs => Body.Instructions;

        internal List<CecILLabel> _Labels = new List<CecILLabel>();
        public ReadOnlyCollection<CecILLabel> Labels => _Labels.AsReadOnly();

        public event Action OnDispose;

        internal bool _ReadOnly = false;

        public CecIL(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        public void Invoke(Manipulator manip) {
            foreach (Instruction instr in Instrs) {
                if (instr.Operand is Instruction target)
                    instr.Operand = new CecILLabel(this, target);
                else if (instr.Operand is Instruction[] targets)
                    instr.Operand = targets.Select(t => new CecILLabel(this, t)).ToArray();
            }

            manip(this);

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is CecILLabel label)
                    instr.Operand = label.Target;
                else if (instr.Operand is CecILLabel[] targets)
                    instr.Operand = targets.Select(l => l.Target).ToArray();
            }

            Method.ConvertShortLongOps();
        }

        public bool MakeReadOnly()
            => _ReadOnly = true;

        public CecILCursor At(int index)
            => At(index == -1 || index == Instrs.Count ? null : Instrs[index]);
        public CecILCursor At(CecILLabel label)
            => At(label.Target);
        public CecILCursor At(Instruction instr)
            => new CecILCursor(this, instr);

        public FieldReference Import(FieldInfo field)
            => Module.ImportReference(field);
        public MethodReference Import(MethodBase method)
            => Module.ImportReference(method);
        public TypeReference Import(Type type)
            => Module.ImportReference(type);

        public CecILLabel DefineLabel()
            => new CecILLabel(this);
        public CecILLabel DefineLabel(Instruction target)
            => new CecILLabel(this, target);

        public void ReplaceOperands(object from, object to) {
            foreach (Instruction instr in Instrs)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
        }

        public IEnumerable<CecILLabel> GetIncomingLabels(Instruction instr)
            => _Labels.Where(l => l.Target == instr);

        public void Dispose() {
            OnDispose?.Invoke();
            OnDispose = null;
        }

    }
}
