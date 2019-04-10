using System;
using System.Reflection;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;
using System.Collections.ObjectModel;

namespace MonoMod.Utils {
    public class MMIL : IDisposable {
        public delegate void Manipulator(MMIL il);

        public MethodDefinition Method { get; }
        public ILProcessor IL { get; private set; }

        public MethodBody Body => Method.Body;
        public ModuleDefinition Module => Method.Module;
        public Mono.Collections.Generic.Collection<Instruction> Instrs => Body.Instructions;

        internal List<MMILLabel> _Labels = new List<MMILLabel>();
        public ReadOnlyCollection<MMILLabel> Labels => _Labels.AsReadOnly();

        public event Action OnDispose;

        public bool IsReadOnly => IL == null;

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

        /// <summary>
        /// Removes the ILProcessor and signifies to the the rest of MonoMod that the method contents have not been altered.
        /// If the method is altered prior to MakeReadOnly, or after by using the MethodBody directly the results are undefined.
        /// </summary>
        public void MakeReadOnly() {
            IL = null;
        }

        [Obsolete("Use new MMILCursor(il).Goto(index)")]
        public MMILCursor At(int index) => 
            new MMILCursor(this).Goto(index);
        [Obsolete("Use new MMILCursor(il).GotoLabel(index)")]
        public MMILCursor At(MMILLabel label) => 
            new MMILCursor(this).GotoLabel(label);
        [Obsolete("Use new MMILCursor(il).GotoLabel(index)")]
        public MMILCursor At(Instruction instr) => 
            new MMILCursor(this).Goto(instr);

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
