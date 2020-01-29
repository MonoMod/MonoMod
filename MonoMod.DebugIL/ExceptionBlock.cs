#if !CECIL0_9
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonoMod.DebugIL {
    internal enum ExceptionBlockType {
        BeginExceptionBlock,
        BeginCatchBlock,
        BeginExceptFilterBlock,
        BeginFaultBlock,
        BeginFinallyBlock,
        EndExceptionBlock
    }

    internal class ExceptionBlock {
        public ExceptionBlockType BlockType;
        public TypeReference CatchType;
    }
}
#endif
