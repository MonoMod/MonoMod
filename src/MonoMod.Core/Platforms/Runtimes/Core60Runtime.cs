using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class Core60Runtime : Core50Runtime {
        public Core60Runtime(ISystem system) : base(system) { }

        // src/coreclr/inc/jiteeversionguid.h line 46
        // 5ed35c58-857b-48dd-a818-7c0136dc9f73
        private static readonly Guid JitVersionGuid = new Guid(
            0x5ed35c58,
            0x857b,
            0x48dd,
            0xa8, 0x18, 0x7c, 0x01, 0x36, 0xdc, 0x9f, 0x73
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override InvokeCompileMethodPtr InvokeCompileMethodPtr => V60.InvokeCompileMethodPtr;

        protected override Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<V60.CompileMethodDelegate>();

        public override RuntimeFeature Features 
            => base.Features & ~RuntimeFeature.RequiresBodyThunkWalking;

        private unsafe IntPtr GetMethodBodyPtr(MethodBase method, RuntimeMethodHandle handle) {
            // This probably applies to some older runtimes too, might even change on a per platform basis, is still fresh,
            // might not work with AOT'd stuff and Wine, but at least it gets us past all the relevant stubs.

            if (method.IsDynamicMethod()) {
                // standard fields (8) | ???? (ptr) | ???? (8) | ???? (ptr) | ???? (ptr) | ???? (ptr) | real ptr
                return *(IntPtr*) ((long) handle.Value + 8 + IntPtr.Size + 8 + IntPtr.Size + IntPtr.Size + IntPtr.Size);
            }

            const int m_wFlags_offset =
                2 // UINT16 m_wFlags3AndTokenRemainder
              + 1 // BYTE m_chunkIndex
              + 1 // BYTE m_bFlags2
              + 2 // WORD m_wSlotNumber
              ;
            var m_wFlags = (ushort*) (((byte*) handle.Value) + m_wFlags_offset);

            // Check for mdcHasNonVtableSlot
            if ((*m_wFlags & 0x0008) == 0x0008) {
                // standard fields (8) | ???? (ptr) | real ptr
                return *(IntPtr*) ((long) handle.Value + 8 + IntPtr.Size);
            }

            // standard fields (8) | real ptr
            return *(IntPtr*) ((long) handle.Value + 8);
        }

        public override unsafe IntPtr GetMethodEntryPoint(MethodBase method) {
            var handle = GetMethodHandle(method);

            GetPtr:
            var ptr = GetMethodBodyPtr(method, handle);
            if (ptr == IntPtr.Zero) { // the method hasn't been JITted yet
                // TODO: call PlatformTriple.Prepare instead to handle generic methods
                RuntimeHelpers.PrepareMethod(handle);
                goto GetPtr;
            }

            return ptr;
        }
    }
}
