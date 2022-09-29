using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using static MonoMod.Core.Interop.Fx;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class FxCLR4Runtime : FxBaseRuntime {

        private ISystem system;

        public FxCLR4Runtime(ISystem system) {
            this.system = system;

            // the only place I could find the actual version number of 4.5 (without just testing myself on a Win7 VM) is here:
            // https://stackoverflow.com/a/11512846
            if (PlatformDetection.Architecture == ArchitectureKind.x86_64 &&
                (PlatformDetection.RuntimeVersion.Revision >= 17379 ||
                PlatformDetection.RuntimeVersion.Minor >= 5) &&
                system.DefaultAbi is { } abi) {
                AbiCore = AbiForCoreFx45X64(abi);
            }
        }


        public override RuntimeFeature Features
            => base.Features & ~RuntimeFeature.RequiresBodyThunkWalking;

        // TODO: check to make sure we're running 4.8 before using this
        private unsafe IntPtr GetMethodBodyPtr(MethodBase method, RuntimeMethodHandle handle) {
            var md = (V48.MethodDesc*) handle.Value;

            md = V48.MethodDesc.FindTightlyBoundWrappedMethodDesc(md);

            var ptr = (IntPtr) md->GetNativeCode();

            return ptr;
        }

        public override unsafe IntPtr GetMethodEntryPoint(MethodBase method) {
            method = GetIdentifiable(method);
            var handle = GetMethodHandle(method);

            var didPrepare = false;
            GetPtr:
            // get then throw away the function pointer to try to ensure that the pointer is restored
            RuntimeHelpers.PrepareMethod(handle);
            _ = handle.GetFunctionPointer();
            var ptr = GetMethodBodyPtr(method, handle);

            if (ptr == IntPtr.Zero) { // the method hasn't been JITted yet
                if (!didPrepare) {
                    // TODO: call PlatformTriple.Prepare instead to handle generic methods
                    RuntimeHelpers.PrepareMethod(handle);
                    didPrepare = true;
                    goto GetPtr;
                } else {
                    // we've already run a prepare, lets try another approach
                    ptr = handle.GetFunctionPointer();

                    throw new InvalidOperationException($"Could not get entry point normally, GetFunctionPointer() = {ptr:x16}");
                }
            }

            return ptr;
        }

        /*public override void DisableInlining(MethodBase method) {
            // the base classes don't specify RuntimeFeature.DisableInlining, so this should never be called
            throw new PlatformNotSupportedException();
        }*/
    }
}
