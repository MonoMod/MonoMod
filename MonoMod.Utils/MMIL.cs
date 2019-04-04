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
    public class MMIL : IDisposable {
        public delegate void Manipulator(MMIL il);

        public MethodDefinition Method { get; private set; }
        public ILProcessor IL { get; private set; }

        public MethodBody Body => Method.Body;
        public ModuleDefinition Module => Method.Module;
        public Mono.Collections.Generic.Collection<Instruction> Instrs => Body.Instructions;

        internal List<MMILLabel> _Labels = new List<MMILLabel>();
        public ReadOnlyCollection<MMILLabel> Labels => _Labels.AsReadOnly();

        public event Action OnDispose;

        public bool IsReadOnly { get; internal set; } = false;

        public MMIL(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        public void Invoke(Manipulator manip) {
            foreach (Instruction instr in Instrs) {
                if (instr.Operand is Instruction target)
                    instr.Operand = new MMILLabel(this, target);
                else if (instr.Operand is Instruction[] targets)
                    instr.Operand = targets.Select(t => new MMILLabel(this, t)).ToArray();
            }

            manip(this);

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is MMILLabel label)
                    instr.Operand = label.Target;
                else if (instr.Operand is MMILLabel[] targets)
                    instr.Operand = targets.Select(l => l.Target).ToArray();
            }

            Method.ConvertShortLongOps();
        }

        public bool MakeReadOnly()
            => IsReadOnly = true;

        public MMILCursor At(int index)
            => At(index == -1 || index == Instrs.Count ? null : Instrs[index]);
        public MMILCursor At(MMILLabel label)
            => At(label.Target);
        public MMILCursor At(Instruction instr)
            => new MMILCursor(this, instr);

        public FieldReference Import(FieldInfo field)
            => Module.ImportReference(field);
        public MethodReference Import(MethodBase method)
            => Module.ImportReference(method);
        public TypeReference Import(Type type)
            => Module.ImportReference(type);

        public MMILLabel DefineLabel()
            => new MMILLabel(this);
        public MMILLabel DefineLabel(Instruction target)
            => new MMILLabel(this, target);

        public void ReplaceOperands(object from, object to) {
            foreach (Instruction instr in Instrs)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
        }

        public IEnumerable<MMILLabel> GetIncomingLabels(Instruction instr)
            => _Labels.Where(l => l.Target == instr);

        public void Dispose() {
            OnDispose?.Invoke();
            OnDispose = null;
        }

    }
}
