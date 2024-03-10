using Mono.Cecil;

namespace MonoMod.DebugIL
{
    internal enum ExceptionBlockType
    {
        BeginExceptionBlock,
        BeginCatchBlock,
        BeginExceptFilterBlock,
        BeginFaultBlock,
        BeginFinallyBlock,
        EndExceptionBlock
    }

    internal class ExceptionBlock
    {
        public ExceptionBlockType BlockType;
        public TypeReference CatchType;
    }
}
