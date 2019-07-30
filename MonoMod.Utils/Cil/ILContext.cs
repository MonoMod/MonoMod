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
    /// <summary>
    /// An IL manipulation "context" with various helpers and direct access to the MethodBody.
    /// </summary>
    public class ILContext : IDisposable {
        /// <summary>
        /// The manipulator callback, accepted by the Invoke method.
        /// </summary>
        /// <param name="il"></param>
        public delegate void Manipulator(ILContext il);

        /// <summary>
        /// The manipulated method.
        /// </summary>
        public MethodDefinition Method { get; private set; }
        /// <summary>
        /// The manipulated method's IL processor.
        /// </summary>
        public ILProcessor IL { get; private set; }

        /// <summary>
        /// The manipulated method body.
        /// </summary>
        public MethodBody Body => Method.Body;
        /// <summary>
        /// The manipulated method's module.
        /// </summary>
        public ModuleDefinition Module => Method.Module;
        /// <summary>
        /// The manipulated method instructions.
        /// </summary>
        public InstrList Instrs => Body.Instructions;

        internal List<ILLabel> _Labels = new List<ILLabel>();
        /// <summary>
        /// A readonly list of all defined labels.
        /// </summary>
        public ReadOnlyCollection<ILLabel> Labels => _Labels.AsReadOnly();

        /// <summary>
        /// Has the context been made read-only? No further method access is possible, but the context has not yet been disposed.
        /// </summary>
        public bool IsReadOnly => IL == null;

        /// <summary>
        /// Events which run when the context will be disposed.
        /// </summary>
        public event Action OnDispose;
        /// <summary>
        /// The current reference bag. Used for methods such as EmitReference and EmitDelegate.
        /// </summary>
        public IILReferenceBag ReferenceBag = NopILReferenceBag.Instance;

        public ILContext(MethodDefinition method) {
            Method = method;
            IL = method.Body.GetILProcessor();
        }

        /// <summary>
        /// Invoke a given manipulator callback.
        /// </summary>
        /// <param name="manip">The manipulator to run in this context.</param>
        public void Invoke(Manipulator manip) {
            if (IsReadOnly)
                throw new InvalidOperationException();

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is Instruction target)
                    instr.Operand = new ILLabel(this, target);
                else if (instr.Operand is Instruction[] targets)
                    instr.Operand = targets.Select(t => new ILLabel(this, t)).ToArray();
            }

            manip(this);

            if (IsReadOnly)
                return;

            foreach (Instruction instr in Instrs) {
                if (instr.Operand is ILLabel label)
                    instr.Operand = label.Target;
                else if (instr.Operand is ILLabel[] targets)
                    instr.Operand = targets.Select(l => l.Target).ToArray();
            }

            Method.FixShortLongOps();
        }

        /// <summary>
        /// Mark this ILContext as read-only and prevent this context from further accessing the originally passed method.
        /// </summary>
        /// <remarks>
        /// If the method is altered prior to calling MakeReadOnly or afterwards by accessing the method directly, the results are undefined.
        /// </remarks>
        public void MakeReadOnly() {
            Method = null;
            IL = null;
            // Labels hold references to Instructions, which can keep
            // all other Instructions in all referenced modules alive.
            // _Labels.Clear doesn't shrink the backing array.
            _Labels = new List<ILLabel>();
        }

        [Obsolete("Use new ILCursor(il).Goto(index)")]
        public ILCursor At(int index) => 
            new ILCursor(this).Goto(index);
        [Obsolete("Use new ILCursor(il).Goto(index)")]
        public ILCursor At(ILLabel label) => 
            new ILCursor(this).GotoLabel(label);
        [Obsolete("Use new ILCursor(il).Goto(index)")]
        public ILCursor At(Instruction instr) => 
            new ILCursor(this).Goto(instr);

        /// <summary>
        /// See <see cref="ModuleDefinition.ImportReference(FieldInfo)"/>
        /// </summary>
        public FieldReference Import(FieldInfo field)
            => Module.ImportReference(field);
        /// <summary>
        /// See <see cref="ModuleDefinition.ImportReference(MethodBase)"/>
        /// </summary>
        public MethodReference Import(MethodBase method)
            => Module.ImportReference(method);
        /// <summary>
        /// See <see cref="ModuleDefinition.ImportReference(Type)"/>
        /// </summary>
        public TypeReference Import(Type type)
            => Module.ImportReference(type);

        /// <summary>
        /// Define a new label to be marked with a cursor.
        /// </summary>
        /// <returns>A label without a target instruction.</returns>
        public ILLabel DefineLabel()
            => new ILLabel(this);
        /// <summary>
        /// Define a new label pointing at a given instruction.
        /// </summary>
        /// <param name="target">The instruction the label will point at.</param>
        /// <returns>A label pointing at the given instruction.</returns>
        public ILLabel DefineLabel(Instruction target)
            => new ILLabel(this, target);

        /// <summary>
        /// Determine the index of a given instruction.
        /// </summary>
        /// <param name="instr">The instruction to get the index of.</param>
        /// <returns>The instruction index, or the end of the method body if it hasn't been found.</returns>
        public int IndexOf(Instruction instr) {
            int index = Instrs.IndexOf(instr);
            return index == -1 ? Instrs.Count : index;
        }

        /// <summary>
        /// Obtain all labels pointing at the given instruction.
        /// </summary>
        /// <param name="instr">The instruction to get all labels for.</param>
        /// <returns>All labels targeting the given instruction.</returns>
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

        /// <summary>
        /// Dispose this context, making it read-only and invoking all OnDispose event listeners.
        /// </summary>
        public void Dispose() {
            OnDispose?.Invoke();
            OnDispose = null;
            MakeReadOnly();
        }

        /// <summary>
        /// Obtain a string representation of this context (method ID and body).
        /// </summary>
        /// <returns>A string representation of this context.</returns>
        public override string ToString() {
            if (Method == null)
                return "// ILContext: READONLY";

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
