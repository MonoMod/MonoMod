using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        private static readonly Type t_Code = typeof(Code);
        private static readonly Type t_OpCodes = typeof(OpCodes);

        private static readonly Dictionary<int, OpCode> _ToLongOp = new Dictionary<int, OpCode>();
        /// <summary>
        /// Get the long form opcode for any short form opcode.
        /// </summary>
        /// <param name="op">The short form opcode.</param>
        /// <returns>The long form opcode.</returns>
        public static OpCode ToLongOp(this OpCode op)
        {
            var name = Enum.GetName(t_Code, op.Code);
            if (name is null || !name.EndsWith("_S", StringComparison.Ordinal))
                return op;
            lock (_ToLongOp)
            {
                if (_ToLongOp.TryGetValue((int)op.Code, out var found))
                    return found;
                return _ToLongOp[(int)op.Code] = (OpCode?)t_OpCodes.GetField(name.Substring(0, name.Length - 2))?.GetValue(null) ?? op;
            }
        }

        private static readonly Dictionary<int, OpCode> _ToShortOp = new Dictionary<int, OpCode>();
        /// <summary>
        /// Get the short form opcode for any long form opcode.
        /// </summary>
        /// <param name="op">The long form opcode.</param>
        /// <returns>The short form opcode.</returns>
        public static OpCode ToShortOp(this OpCode op)
        {
            var name = Enum.GetName(t_Code, op.Code);
            if (name is null || name.EndsWith("_S", StringComparison.Ordinal))
                return op;
            lock (_ToShortOp)
            {
                if (_ToShortOp.TryGetValue((int)op.Code, out var found))
                    return found;
                return _ToShortOp[(int)op.Code] = (OpCode?)t_OpCodes.GetField(name + "_S")?.GetValue(null) ?? op;
            }
        }


        /// <summary>
        /// Calculate updated instruction offsets. Required for certain manual fixes.
        /// </summary>
        /// <param name="method">The method to recalculate the IL instruction offsets for.</param>
        public static void RecalculateILOffsets(this MethodDefinition method)
        {
            Helpers.ThrowIfArgumentNull(method);
            if (!method.HasBody)
                return;

            var offs = 0;
            for (var i = 0; i < method.Body.Instructions.Count; i++)
            {
                var instr = method.Body.Instructions[i];
                instr.Offset = offs;
                offs += instr.GetSize();
            }
        }

        /// <summary>
        /// Fix (and optimize) any instructions which should use the long / short form opcodes instead.
        /// </summary>
        /// <param name="method">The method to apply the fixes to.</param>
        public static void FixShortLongOps(this MethodDefinition method)
        {
            Helpers.ThrowIfArgumentNull(method);
            if (!method.HasBody)
                return;

            // Convert short to long ops.
            for (var i = 0; i < method.Body.Instructions.Count; i++)
            {
                var instr = method.Body.Instructions[i];
                if (instr.Operand is Instruction)
                {
                    instr.OpCode = instr.OpCode.ToLongOp();
                }
            }

            method.RecalculateILOffsets();

            // Optimize long to short ops.
            bool optimized;
            do
            {
                optimized = false;
                for (var i = 0; i < method.Body.Instructions.Count; i++)
                {
                    var instr = method.Body.Instructions[i];
                    // Change short <-> long operations as the method grows / shrinks.
                    if (instr.Operand is Instruction target)
                    {
                        // Thanks to Chicken Bones for helping out with this!
                        var distance = target.Offset - (instr.Offset + instr.GetSize());
                        if (distance == (sbyte)distance)
                        {
                            var prev = instr.OpCode;
                            instr.OpCode = instr.OpCode.ToShortOp();
                            optimized = prev != instr.OpCode;
                        }
                    }
                }
            } while (optimized);
        }

    }
}
