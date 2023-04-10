using MonoMod.Core.Interop;
using MonoMod.Utils;
using System;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core31_31Runtime : Core31Runtime {
        public Core31_31Runtime(ISystem system) : base(system) { }

        protected override CoreCLR.InvokeCompileMethodPtr InvokeCompileMethodPtr => CoreCLR.V31.InvokeCompileMethodPtr;

        // 0ba106c8-81a0-407f-99a1-928448c1eb62
        protected override Guid ExpectedJitVersion => new Guid(
            0x0ba106c8,
            0x81a0,
            0x407f,
            0x99, 0xa1, 0x92, 0x84, 0x48, 0xc1, 0xeb, 0x62);

        protected override Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<CoreCLR.V31.CompileMethodDelegate>();
    }
}