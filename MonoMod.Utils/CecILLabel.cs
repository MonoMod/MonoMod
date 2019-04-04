using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;

namespace MonoMod.Utils {
    public sealed class CecILLabel {

        private readonly CecIL Context;
        public Instruction Target;
        
        public IEnumerable<CecILCursor> Branches
            => Context.Instrs.Where(i => i.Operand == this).Select(i => new CecILCursor(Context, i));

        internal CecILLabel(CecIL context) {
            Context = context;
            Context._Labels.Add(this);
        }

        internal CecILLabel(CecIL context, Instruction target)
            : this(context) {
            Target = target;
        }
    }
}
