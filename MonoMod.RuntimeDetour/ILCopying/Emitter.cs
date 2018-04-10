using System.Linq;
using System.Reflection.Emit;

namespace Harmony.ILCopying {
    static class Emitter
	{
        public static string FormatArgument(object argument)
		{
			if (argument == null) return "NULL";
			var type = argument.GetType();

			if (type == typeof(string))
				return "\"" + argument + "\"";
			if (type == typeof(Label))
				return "Label" + ((Label)argument).GetHashCode();
			if (type == typeof(Label[]))
				return "Labels" + string.Join(",", ((Label[])argument).Select(l => l.GetHashCode().ToString()).ToArray());
			if (type == typeof(LocalBuilder))
				return ((LocalBuilder)argument).LocalIndex + " (" + ((LocalBuilder)argument).LocalType + ")";

			return argument.ToString().Trim();
		}

		public static void MarkBlockBefore(ILGenerator il, ExceptionBlock block, out Label? label)
		{
			label = null;
			switch (block.blockType)
			{
				case ExceptionBlockType.BeginExceptionBlock:
					label = il.BeginExceptionBlock();
					return;

				case ExceptionBlockType.BeginCatchBlock:
					il.BeginCatchBlock(block.catchType);
					return;

				case ExceptionBlockType.BeginExceptFilterBlock:
					il.BeginExceptFilterBlock();
					return;

				case ExceptionBlockType.BeginFaultBlock:
					il.BeginFaultBlock();
					return;

				case ExceptionBlockType.BeginFinallyBlock:
					il.BeginFinallyBlock();
					return;
			}
		}

		public static void MarkBlockAfter(ILGenerator il, ExceptionBlock block)
		{
			if (block.blockType == ExceptionBlockType.EndExceptionBlock)
			{
				il.EndExceptionBlock();
			}
		}
	}
}