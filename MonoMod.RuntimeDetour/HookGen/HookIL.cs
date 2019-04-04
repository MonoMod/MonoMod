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
    [Obsolete("Use MMIL from MonoMod.Utils instead.")]
    public class HookIL : IDisposable {

        public MethodDefinition Method { get; private set; }
        public ILProcessor IL { get; private set; }

        public MethodBody Body => Method.Body;
        public ModuleDefinition Module => Method.Module;
        public Mono.Collections.Generic.Collection<Instruction> Instrs => Body.Instructions;

        internal List<HookILLabel> _Labels = new List<HookILLabel>();
        public ReadOnlyCollection<HookILLabel> Labels => _Labels.AsReadOnly();

        public event Action OnDispose;

        internal bool _ReadOnly = false;

        public HookIL(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        public void Invoke(ILManipulator manip) {
            foreach (Instruction instr in Instrs) {
                if (instr.Operand is Instruction target)
                    instr.Operand = new HookILLabel(this, target);
                else if (instr.Operand is Instruction[] targets)
                    instr.Operand = targets.Select(t => new HookILLabel(this, t)).ToArray();
            }

            manip(this);

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is HookILLabel label)
                    instr.Operand = label.Target;
                else if (instr.Operand is HookILLabel[] targets)
                    instr.Operand = targets.Select(l => l.Target).ToArray();
            }

            Method.ConvertShortLongOps();
        }

        public bool MakeReadOnly()
            => _ReadOnly = true;

        public HookILCursor At(int index)
            => At(index == -1 || index == Instrs.Count ? null : Instrs[index]);
        public HookILCursor At(HookILLabel label)
            => At(label.Target);
        public HookILCursor At(Instruction instr)
            => new HookILCursor(this, instr);

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

        public void Dispose() {
            OnDispose?.Invoke();
            OnDispose = null;
        }

    }
}
