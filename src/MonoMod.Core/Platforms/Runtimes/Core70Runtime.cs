using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Reflection;
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

        protected override MethodInfo GetOrCreateGetTypeFromHandleUnsafe()
        {
            // starting in .NET 7, the BCL actually exposes helpers for this. Lets use those
            // instead of hitting the icalls directly.
            var getRth = typeof(RuntimeTypeHandle).GetMethod("FromIntPtr", BindingFlags.Static | BindingFlags.Public, null, [typeof(IntPtr)], null);
            Helpers.Assert(getRth is not null);

            var getTypeFromHandle = ((Delegate)Type.GetTypeFromHandle).Method;

            using (var dmd = new DynamicMethodDefinition("Type.GetTypeFromHandle", typeof(Type), [typeof(IntPtr)]))
            {
                var il = dmd.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, dmd.Module.ImportReference(getRth));
                il.Emit(OpCodes.Call, dmd.Module.ImportReference(getTypeFromHandle));
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }
        }
    }
}
