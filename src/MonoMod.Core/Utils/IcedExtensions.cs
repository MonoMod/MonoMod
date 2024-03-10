using Iced.Intel;
using System;
using System.Collections.Generic;

namespace MonoMod.Core.Utils
{
    internal static class IcedExtensions
    {
#if !DEBUG
        [Obsolete("This method is not supported.", error: true)]
#endif
        public static string FormatInsns(this IList<Instruction> insns)
        {
#if DEBUG
            var formatter = new NasmFormatter();
            var output = new StringOutput();
            foreach (var ins in insns)
            {
                formatter.Format(ins, output);
                output.Write(Environment.NewLine, FormatterTextKind.Text);
            }
            return output.ToString();
#else
            throw new NotSupportedException();
#endif
        }

#if !DEBUG
        [Obsolete("This method is not supported.", error: true)]
#endif
        public static string FormatInsns(this InstructionList insns)
        {
#if DEBUG
            var formatter = new NasmFormatter();
            var output = new StringOutput();
            foreach (ref var ins in insns)
            {
                formatter.Format(ins, output);
                output.Write(Environment.NewLine, FormatterTextKind.Text);
            }
            return output.ToString();
#else
            throw new NotSupportedException();
#endif
        }
    }
}
