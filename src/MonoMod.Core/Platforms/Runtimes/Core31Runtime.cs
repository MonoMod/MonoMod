using MonoMod.Utils;
using System;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core31Runtime : Core30Runtime
    {
        public Core31Runtime(ISystem system) : base(system) { }

        protected override InvokeCompileMethodPtr InvokeCompileMethodPtr => V31.InvokeCompileMethodPtr;

        protected override Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<V31.CompileMethodDelegate>();
    }
}
