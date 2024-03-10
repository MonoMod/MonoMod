using MonoMod.Utils;
using System;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core50Runtime : Core31Runtime
    {
        public Core50Runtime(ISystem system) : base(system) { }

        // src/coreclr/src/inc/corinfo.h line 211
        // a5eec3a4-4176-43a7-8c2b-a05b551d4f49
        private static readonly Guid JitVersionGuid = new Guid(
            0xa5eec3a4,
            0x4176,
            0x43a7,
            0x8c, 0x2b, 0xa0, 0x5b, 0x55, 0x1d, 0x4f, 0x49
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;
        protected override int VtableIndexICorJitCompilerGetVersionGuid => 2;

        protected override InvokeCompileMethodPtr InvokeCompileMethodPtr => V50.InvokeCompileMethodPtr;

        protected override Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<V50.CompileMethodDelegate>();
    }
}
