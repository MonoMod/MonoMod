using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;

namespace MonoMod.Utils {
    public sealed class MMILLabel {

        private readonly MMIL Context;
        public Instruction Target;
        
        public IEnumerable<Instruction> Branches
            => Context.Instrs.Where(i => i.Operand == this);

        internal MMILLabel(MMIL context) {
            Context = context;
            Context._Labels.Add(this);
        }

        internal MMILLabel(MMIL context, Instruction target)
            : this(context) {
            Target = target;
        }
    }
}
