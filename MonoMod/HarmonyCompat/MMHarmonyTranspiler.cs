using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using StringInject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonoMod.NET40Shim;
using MonoMod.Helpers;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Collections;

namespace MonoMod.HarmonyCompat {
    public class MMHarmonyInstruction {
        public System.Reflection.Emit.OpCode opcode;
        public object operand;
        public List<Label> labels = new List<Label>();
    }
    public class MMHarmonyTranspiler : IEnumerator<MMHarmonyInstruction> {

        public MMHarmonyInstruction Current { get; private set; }

        private int _Index;

        public bool MoveNext() {
            return false;
        }

        object IEnumerator.Current {
            get {
                return Current;
            }
        }

        public void Dispose() {
            throw new InvalidOperationException();
        }

        public void Reset() {
            throw new InvalidOperationException();
        }

    }
}
