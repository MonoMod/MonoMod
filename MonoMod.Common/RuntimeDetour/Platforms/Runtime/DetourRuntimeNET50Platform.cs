using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.RuntimeDetour.Platforms {
    // This is based on the Core 3.1 implementation for now
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNET50Platform : DetourRuntimeNETCore30Platform {
        // As of .NET 5, this GUID is found at src/coreclr/src/inc/corinfo.h as JITEEVersionIdentifier
        public static new readonly Guid JitVersionGuid = new Guid("a5eec3a4-4176-43a7-8c2b-a05b551d4f49");

        // TODO: Override the implementations to make it work on NET5

        private static FieldInfo _runtimeAssemblyPtrField = Type.GetType("System.Reflection.RuntimeAssembly").GetField("m_assembly", BindingFlags.Instance | BindingFlags.NonPublic);
        protected override unsafe void MakeAssemblySystemAssembly(Assembly assembly) {

            // RuntimeAssembly.m_assembly is a DomainAssembly*,
            // which contains an Assembly*,
            // which contains a PEAssembly*,
            // which is a subclass of PEFile
            // which has a `flags` field, with bit 0x01 representing 'system'

            const int PEFILE_SYSTEM = 0x01;

            IntPtr domAssembly = (IntPtr) _runtimeAssemblyPtrField.GetValue(assembly);

            // DomainAssembly in src/coreclr/src/vm/domainfile.h
            int domOffset =
                IntPtr.Size + // VTable ptr
                // DomainFile
                IntPtr.Size + // PTR_AppDomain               m_pDomain;
                IntPtr.Size + // PTR_PEFile                  m_pFile;
                IntPtr.Size + // PTR_PEFile                  m_pOriginalFile;
                IntPtr.Size + // PTR_Module                  m_pModule;
                sizeof(int) + // FileLoadLevel               m_level; // FileLoadLevel is an enum with unspecified type; I assume it defaults to 'int' because that's what `enum class` does
                IntPtr.Size + // LOADERHANDLE                m_hExposedModuleObject;
                IntPtr.Size + // ExInfo* m_pError;
                sizeof(int) + // DWORD                    m_notifyflags;
                sizeof(int) + // BOOL                        m_loading; // no matter the actual size of this BOOL, the next member is a pointer, and we'd always be misaligned
                IntPtr.Size + // DynamicMethodTable * m_pDynamicMethodTable;
                IntPtr.Size + // class UMThunkHash *m_pUMThunkHash;
                sizeof(int) + // BOOL m_bDisableActivationCheck;
                sizeof(int) + // DWORD m_dwReasonForRejectingNativeImage;
                // DomainAssembly
                IntPtr.Size + // LOADERHANDLE                            m_hExposedAssemblyObject;
                0; // here is our Assembly*
            
            if (PlatformHelper.Is(Platform.Bits64)) {
                domOffset +=
                    sizeof(int); // padding to align the next TADDR (which is a void*) (m_hExposedModuleObject)
            }

            IntPtr pAssembly = *(IntPtr*) (((byte*) domAssembly) + domOffset);

            // Assembly in src/coreclr/src/vm/assembly.hpp
            int pAssemOffset =
                IntPtr.Size + // PTR_BaseDomain        m_pDomain;
                IntPtr.Size + // PTR_ClassLoader       m_pClassLoader;
                IntPtr.Size + // PTR_MethodDesc        m_pEntryPoint;
                IntPtr.Size + // PTR_Module            m_pManifest;
                0; // here is out PEAssembly* (manifestFile)

            IntPtr peAssembly = *(IntPtr*) (((byte*) pAssembly) + pAssemOffset);

            // PEAssembly in src/coreclr/src/vm/pefile.h
            int peAssemOffset =
                IntPtr.Size + // VTable ptr
                // PEFile
                IntPtr.Size + // PTR_PEImage              m_identity;
                IntPtr.Size + // PTR_PEImage              m_openedILimage;
                sizeof(int) + // BOOL                     m_MDImportIsRW_Debugger_Use_Only; // i'm pretty sure that these bools are sizeof(int)
                sizeof(int) + // Volatile<BOOL>           m_bHasPersistentMDImport;         // but they might not be, and it might vary (that would be a pain in the ass)
                IntPtr.Size + // IMDInternalImport       *m_pMDImport;
                IntPtr.Size + // IMetaDataImport2        *m_pImporter;
                IntPtr.Size + // IMetaDataEmit           *m_pEmitter;
                IntPtr.Size + // SimpleRWLock            *m_pMetadataLock;
                sizeof(int) + // Volatile<LONG>           m_refCount; // fuck C long
                + 0; // here is out int (flags)

            int* flags = (int*) (((byte*) peAssembly) + peAssemOffset);
            *flags |= PEFILE_SYSTEM;
        }
    }
}
