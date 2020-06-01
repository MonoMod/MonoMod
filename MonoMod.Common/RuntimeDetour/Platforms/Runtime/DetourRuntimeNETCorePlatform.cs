using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.Utils;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using MonoMod.Common.RuntimeDetour.Platforms.Runtime;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNETCorePlatform : DetourRuntimeNETPlatform {

        // All of this stuff is for JIT hooking in RuntimeDetour so we can update hooks when a method is re-jitted
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr d_getJit();
        private static d_getJit getJit;

        protected static IntPtr GetJitObject() {
            if (getJit == null) {
                // To make sure we get the right clrjit, we enumerate the process's modules and find the one 
                //   with the name we care aboutm, then use its full path to gat a handle and load symbols.
                Process currentProc = Process.GetCurrentProcess();
                ProcessModule clrjitModule = currentProc.Modules.Cast<ProcessModule>()
                    .FirstOrDefault(m => Path.GetFileNameWithoutExtension(m.FileName).EndsWith("clrjit"));
                if (clrjitModule == null)
                    throw new PlatformNotSupportedException();

                if (!DynDll.TryOpenLibrary(clrjitModule.FileName, out IntPtr clrjitPtr))
                    throw new PlatformNotSupportedException();

                try {
                    getJit = clrjitPtr.GetFunction(nameof(getJit)).AsDelegate<d_getJit>();
                } catch {
                    DynDll.CloseLibrary(clrjitPtr);
                    throw;
                }
            }

            return getJit();
        }

        protected static Guid GetJitGuid(IntPtr jit) {
            d_getVersionIdentifier getVersionIdentifier = ReadObjectVTable(jit, vtableIndex_ICorJitCompiler_getVersionIdentifier)
                .AsDelegate<d_getVersionIdentifier>();
            getVersionIdentifier(jit, out Guid guid);
            return guid;
        }

        // FIXME: .NET 5 has this method at index 2; how do we identify this?
        private const int vtableIndex_ICorJitCompiler_getVersionIdentifier = 4;
        private const int vtableIndex_ICorJitCompiler_getVersionIdentifier_net5 = 2;
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void d_getVersionIdentifier(
            IntPtr thisPtr, // ICorJitCompiler*
            out Guid versionIdentifier
            );

        protected const int vtableIndex_ICorJitCompiler_compileMethod = 0;

        protected static unsafe IntPtr* GetVTableEntry(IntPtr @object, int index)
            => (*(IntPtr**) @object) + index;
        protected static unsafe IntPtr ReadObjectVTable(IntPtr @object, int index)
            => *GetVTableEntry(@object, index);

        protected override void DisableInlining(MethodBase method, RuntimeMethodHandle handle) {
            // https://github.com/dotnet/runtime/blob/89965be3ad2be404dc82bd9e688d5dd2a04bcb5f/src/coreclr/src/vm/method.hpp#L178
            // mdcNotInline = 0x2000
            // References to RuntimeMethodHandle (CORINFO_METHOD_HANDLE) pointing to MethodDesc
            // can be traced as far back as https://ntcore.com/files/netint_injection.htm

            // FIXME: Take a very educated guess regarding the offset to m_wFlags in MethodDesc.
        }

        public static readonly Guid Core31Jit = new Guid("d609bed1-7831-49fc-bd49-b6f054dd4d46");
        public static readonly Guid Net50p4Jit = new Guid("8b2226a2-ac30-4f5c-ae5c-926c792ecdb9");

        protected virtual void InstallJitHooks(IntPtr jitObject) => throw new PlatformNotSupportedException();

        public static DetourRuntimeNETCorePlatform Create() {
            try {
                IntPtr jit = GetJitObject();
                Guid jitGuid = GetJitGuid(jit);

                DetourRuntimeNETCorePlatform platform = new DetourRuntimeNETCorePlatform();

                if (jitGuid == Net50p4Jit) {
                    platform = new DetourRuntimeNET50p4Platform();
                } if (jitGuid == Core31Jit) {
                    platform = new DetourRuntimeNET50p4Platform();
                }
                // TODO: add more known JIT GUIDs

                platform?.InstallJitHooks(jit);
                return platform;
            } catch {
                MMDbgLog.Log("Could not get JIT information for the runtime, falling out to the version without JIT hooks");
            }

            return new DetourRuntimeNETCorePlatform();
        }
    }
}
