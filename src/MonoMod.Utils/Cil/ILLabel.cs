using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.Linq;

namespace MonoMod.Cil
{
    /// <summary>
    /// A label to be used in ILContexts.
    /// </summary>
    public sealed class ILLabel
    {

        private readonly ILContext Context;
        /// <summary>
        /// The target instruction this label points at.
        /// </summary>
        public Instruction? Target { get; set; }

        /// <summary>
        /// All instructions using this label.
        /// </summary>
        public IEnumerable<Instruction> Branches
            => Context.Instrs.Where(i => i.Operand == this);

        internal ILLabel(ILContext context)
        {
            Context = context;
            Context._Labels.Add(this);
        }

        internal ILLabel(ILContext context, Instruction? target)
            : this(context)
        {
            Target = target;
        }
    }
}
