using System;
using System.Reflection;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Linq;
using System.Collections.ObjectModel;
using InstrList = Mono.Collections.Generic.Collection<Mono.Cecil.Cil.Instruction>;
using MonoMod.Utils;
using System.Text;

namespace MonoMod.Cil {
    public class ILContext : IDisposable {
        public delegate void Manipulator(ILContext il);

        public MethodDefinition Method { get; }
        public ILProcessor IL { get; private set; }

        public MethodBody Body => Method.Body;
        public ModuleDefinition Module => Method.Module;
        public InstrList Instrs => Body.Instructions;

        internal List<ILLabel> _Labels = new List<ILLabel>();
        public ReadOnlyCollection<ILLabel> Labels => _Labels.AsReadOnly();

        public bool IsReadOnly => IL == null;

        public event Action OnDispose;
        public IILReferenceBag ReferenceBag = NopILReferenceBag.Instance;

        public ILContext(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        public void Invoke(Manipulator manip) {
            foreach (Instruction instr in Instrs) {
                if (instr.Operand is Instruction target)
                    instr.Operand = new ILLabel(this, target);
                else if (instr.Operand is Instruction[] targets)
                    instr.Operand = targets.Select(t => new ILLabel(this, t)).ToArray();
            }

            manip(this);

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is ILLabel label)
                    instr.Operand = label.Target;
                else if (instr.Operand is ILLabel[] targets)
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

        [Obsolete("Use new ILCursor(il).Goto(index)")]
        public ILCursor At(int index) => 
            new ILCursor(this).Goto(index);
        [Obsolete("Use new ILCursor(il).GotoLabel(index)")]
        public ILCursor At(ILLabel label) => 
            new ILCursor(this).GotoLabel(label);
        [Obsolete("Use new ILCursor(il).GotoLabel(index)")]
        public ILCursor At(Instruction instr) => 
            new ILCursor(this).Goto(instr);

        public FieldReference Import(FieldInfo field)
            => Module.ImportReference(field);
        public MethodReference Import(MethodBase method)
            => Module.ImportReference(method);
        public TypeReference Import(Type type)
            => Module.ImportReference(type);

        public ILLabel DefineLabel()
            => new ILLabel(this);
        public ILLabel DefineLabel(Instruction target)
            => new ILLabel(this, target);

        public int IndexOf(Instruction instr) {
            int index = Instrs.IndexOf(instr);
            return index == -1 ? Instrs.Count : index;
        }

        public void ReplaceOperands(object from, object to) {
            foreach (Instruction instr in Instrs)
                if (instr.Operand?.Equals(from) ?? from == null)
                    instr.Operand = to;
        }

        public IEnumerable<ILLabel> GetIncomingLabels(Instruction instr)
            => _Labels.Where(l => l.Target == instr);

        /// <summary>
        /// Bind an arbitary object to an ILContext for static retrieval.
        /// </summary>
        /// <typeparam name="T">The type of the object. The combination of typeparam and id provides the unique static reference.</typeparam>
        /// <param name="t">The object to store.</param>
        /// <returns>The id to use in combination with the typeparam for object retrieval.</returns>
        public int AddReference<T>(T t) {
            IILReferenceBag bag = ReferenceBag;
            int id = bag.Store(t);
            OnDispose += () => bag.Clear<T>(id);
            return id;
        }

        public void Dispose() {
            OnDispose?.Invoke();
            OnDispose = null;
        }

        public override string ToString() {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine($"// ILContext: {Method}");
            foreach (Instruction instr in Instrs)
                ToString(builder, instr);

            return builder.ToString();
        }

        internal static StringBuilder ToString(StringBuilder builder, Instruction instr) {
            if (instr == null)
                return builder;

            object operand = instr.Operand;
            if (operand is ILLabel label)
                instr.Operand = label.Target;
            else if (operand is ILLabel[] labels)
                instr.Operand = labels.Select(l => l.Target).ToArray();

            builder.AppendLine(instr.ToString());

            instr.Operand = operand;
            return builder;
        }

    }
}
