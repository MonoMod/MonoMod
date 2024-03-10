using MonoMod.Utils;
using System;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core30Runtime : Core21Runtime
    {
        public Core30Runtime(ISystem system) : base(system) { }


        // src/inc/corinfo.h line 220
        // d609bed1-7831-49fc-bd49-b6f054dd4d46
        private static readonly Guid JitVersionGuid = new Guid(
            0xd609bed1,
            0x7831,
            0x49fc,
            0xbd, 0x49, 0xb6, 0xf0, 0x54, 0xdd, 0x4d, 0x46
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override InvokeCompileMethodPtr InvokeCompileMethodPtr => V30.InvokeCompileMethodPtr;

        protected override Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<V30.CompileMethodDelegate>();
    }
}
