// FIXME: MERGE MonoModExt AND Extensions, KEEP ONLY WHAT'S NEEDED!

using System;
using System.Reflection;
using SRE = System.Reflection.Emit;
using CIL = Mono.Cecil.Cil;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Mono.Cecil;
using System.Text;
using Mono.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils {
    public static partial class Extensions {

        private static readonly Type t_Code = typeof(Code);
        private static readonly Type t_OpCodes = typeof(OpCodes);

        private static readonly Dictionary<int, OpCode> _ToLongOp = new Dictionary<int, OpCode>();
        /// <summary>
        /// Get the long form opcode for any short form opcode.
        /// </summary>
        /// <param name="op">The short form opcode.</param>
        /// <returns>The long form opcode.</returns>
        public static OpCode ToLongOp(this OpCode op) {
            string name = Enum.GetName(t_Code, op.Code);
            if (!name.EndsWith("_S"))
                return op;
            lock (_ToLongOp) {
                if (_ToLongOp.TryGetValue((int) op.Code, out OpCode found))
                    return found;
                return _ToLongOp[(int) op.Code] = (OpCode?) t_OpCodes.GetField(name.Substring(0, name.Length - 2))?.GetValue(null) ?? op;
            }
        }

        private static readonly Dictionary<int, OpCode> _ToShortOp = new Dictionary<int, OpCode>();
        /// <summary>
        /// Get the short form opcode for any long form opcode.
        /// </summary>
        /// <param name="op">The long form opcode.</param>
        /// <returns>The short form opcode.</returns>
        public static OpCode ToShortOp(this OpCode op) {
            string name = Enum.GetName(t_Code, op.Code);
            if (name.EndsWith("_S"))
                return op;
            lock (_ToShortOp) {
                if (_ToShortOp.TryGetValue((int) op.Code, out OpCode found))
                    return found;
                return _ToShortOp[(int) op.Code] = (OpCode?) t_OpCodes.GetField(name + "_S")?.GetValue(null) ?? op;
            }
        }

    }
}
