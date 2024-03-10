using MonoMod.Utils;
using System;
#if NET6_USE_RUNTIME_INTROSPECTION
using System.Reflection;
using System.Runtime.CompilerServices;
#endif
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core60Runtime : Core50Runtime
    {
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

#if NET6_USE_RUNTIME_INTROSPECTION
        public override RuntimeFeature Features 
            => base.Features & ~RuntimeFeature.RequiresBodyThunkWalking;

        private unsafe IntPtr GetMethodBodyPtr(MethodBase method, RuntimeMethodHandle handle) {
            var md = (V60.MethodDesc*) handle.Value;

            md = V60.MethodDesc.FindTightlyBoundWrappedMethodDesc(md);

            return (IntPtr) md->GetNativeCode();
        }

        public override unsafe IntPtr GetMethodEntryPoint(MethodBase method) {
            method = GetIdentifiable(method);
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
#endif
    }
}
