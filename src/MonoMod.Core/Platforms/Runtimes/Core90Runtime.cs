using System;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core90Runtime : Core80Runtime
    {
        public Core90Runtime(ISystem system, IArchitecture arch) : base(system, arch) { }

        // src/coreclr/inc/jiteeversionguid.h line 46
        // d6218a78-9a34-4c6f-8db5-077a06022fae
        private static readonly Guid JitVersionGuid = new(
            0xd6218a78,
            0x9a34,
            0x4c6f,
            0x8d, 0xb5, 0x07, 0x7a, 0x06, 0x02, 0x2f, 0xae
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override int VtableIndexICorJitInfoAllocMem => V90.ICorJitInfoVtable.AllocMemIndex;
        protected override int ICorJitInfoFullVtableCount => V90.ICorJitInfoVtable.TotalVtableCount;
    }
}
