using System;
using System.Diagnostics.CodeAnalysis;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    [SuppressMessage("Performance", "CA1852", Justification = "This type will be derived for .NET 12.")]
    internal class Core110Runtime : Core100Runtime
    {
        public Core110Runtime(ISystem system, IArchitecture arch) : base(system, arch) { }
        
        // src/coreclr/inc/jiteeversionguid.h line 46
        // 4b5934bd-e9a2-4376-bfbf-d4739f11bdb0
        private static readonly Guid JitVersionGuid = new(
            0x4b5934bd,
            0xe9a2,
            0x4376,
            0xbf, 0xbf, 0xd4, 0x73, 0x9f, 0x11, 0xbd, 0xb0
        );
        
        protected override Guid ExpectedJitVersion => JitVersionGuid;
        
        protected override int VtableIndexICorJitInfoAllocMem => V110.ICorJitInfoVtable.AllocMemIndex;
        protected override int ICorJitInfoFullVtableCount => V110.ICorJitInfoVtable.TotalVtableCount;
    }
}