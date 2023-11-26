using System;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes {
    internal sealed class Core80Runtime : Core70Runtime {
        public Core80Runtime(ISystem system, IArchitecture arch) : base(system, arch) { }

        // src/coreclr/inc/jiteeversionguid.h line 46
        // 4bceb905-d550-4a5d-b1eb-276fff68d183
        private static readonly Guid JitVersionGuid = new Guid(
            0x4bceb905,
            0xd550,
            0x4a5d,
            0xb1, 0xeb, 0x27, 0x6f, 0xff, 0x68, 0xd1, 0x83
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override int VtableIndexICorJitInfoAllocMem => V80.ICorJitInfoVtableV80.AllocMemIndex;
        protected override int ICorJitInfoFullVtableCount => V80.ICorJitInfoVtableV80.TotalVtableCount;
    }
}
