using System;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class Core70Runtime : Core60Runtime {
        public Core70Runtime(ISystem system) : base(system) {}

        // src/coreclr/inc/jiteeversionguid.h line 46
        // 1b9551b8-21f4-4233-9c90-f3eabd6a322b
        private static readonly Guid JitVersionGuid = new Guid(
            0x1b9551b8,
            0x21f4,
            0x4233,
            0x9c, 0x90, 0xf3, 0xea, 0xbd, 0x6a, 0x32, 0x2b
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;
    }
}
