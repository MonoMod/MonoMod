using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MMIL {
    public class Inject : IDisposable {

        internal Inject(string il, bool regex = false) {
            throw new InvalidProgramException($"{GetType().FullName} must be used in a MonoMod context!");
        }

        public void Dispose() {
        }

    }

    public class InjectBefore : Inject {
        public InjectBefore(string il, bool regex = false)
            : base(il, regex) {
        }
    }

    public class InjectAfter : Inject {
        public InjectAfter(string il, bool regex = false)
            : base(il, regex) {
        }
    }

    public class InjectReplace : Inject {
        public InjectReplace(string il, bool regex = false)
            : base(il, regex) {
        }
    }
}
