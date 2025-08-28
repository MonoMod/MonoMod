using System;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core70Runtime : Core60Runtime
    {
        public Core70Runtime(ISystem system, IArchitecture arch) : base(system, arch) { }

        // src/coreclr/inc/jiteeversionguid.h line 46
        // 6be47e5d-a92b-4d16-9280-f63df646ada4
        private static readonly Guid JitVersionGuid = new(
            0x6be47e5d,
            0xa92b,
            0x4d16,
            0x92, 0x80, 0xf6, 0x3d, 0xf6, 0x46, 0xad, 0xa4
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override int VtableIndexICorJitInfoAllocMem => V70.ICorJitInfoVtable.AllocMemIndex;
        protected override int ICorJitInfoFullVtableCount => V70.ICorJitInfoVtable.TotalVtableCount;
    }
}
