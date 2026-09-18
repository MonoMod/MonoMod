using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    [SuppressMessage("Performance", "CA1852", Justification = "This type will be derived for .NET 11.")]
    internal class Core100Runtime : Core90Runtime
    {
        public Core100Runtime(ISystem system, IArchitecture arch) : base(system, arch) { }
        
        // src/coreclr/inc/jiteeversionguid.h line 46
        // 7a8cbc56-9e19-4321-80b9-a0d2c578c945
        private static readonly Guid JitVersionGuid = new(
            0x7a8cbc56,
            0x9e19,
            0x4321,
            0x80, 0xb9, 0xa0, 0xd2, 0xc5, 0x78, 0xc9, 0x45
        );
        
        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override int VtableIndexICorJitInfoAllocMem => V100.ICorJitInfoVtable.AllocMemIndex;
        protected override int ICorJitInfoFullVtableCount => V100.ICorJitInfoVtable.TotalVtableCount;

        protected override unsafe void MakeAssemblySystemAssembly(Assembly assembly)
        {
            // RuntimeAssembly.m_assembly is an Assembly*,
            // which contains a PEAssembly*,
            // which contains a bool m_isSystem.

            var pAssembly = (IntPtr)RuntimeAssemblyPtrField.GetValue(assembly)!;

            // Assembly in src/coreclr/vm/assembly.hpp
            var pAssemOffset =
                IntPtr.Size + // PTR_ClassLoader       m_pClassLoader;
                IntPtr.Size + // PTR_MethodDesc        m_pEntryPoint;
                IntPtr.Size + // PTR_Module            m_pModule;
                0; // here is out PEAssembly* (m_pPEAssembly)

            var peAssembly = *(IntPtr*)(((byte*)pAssembly) + pAssemOffset);

            // PEAssembly in src/coreclr/vm/peassembly.h
            var peAssemOffset =
                IntPtr.Size + // VTable ptr
                              // PEFile
                (IsDebugClr ? 0 + // #ifdef _DEBUG 
                    IntPtr.Size + // LPCWSTR             m_pDebugName;
                                  // SBuffer // src/coreclr/vm/sbuffer.h
                    sizeof(int) + // COUNT_T             m_size; // COUNT_T is a typedef of uint32_t
                    sizeof(int) + // COUNT_T             m_allocation;
                    sizeof(int) + // UINT32              m_flags;
                                  //sizeof(int) + // padding to 8 bytes
                    IntPtr.Size + // union { BYTE* m_buffer; WCHAR* m_asStr; };
                    sizeof(int) + // int                 m_revision
                                  // SString (itself empty, only base type SBuffer has data)
                                  // SString             m_debugName; // src/coreclr/vm/sstring.h
                                  //sizeof(int) + // padding to 8 bytes
                0 : 0) +          // #endif
                IntPtr.Size + // PTR_PEImage              m_PEImage;
                sizeof(int) + // BOOL                     m_MDImportIsRW_Debugger_Use_Only; // i'm pretty sure that these bools are sizeof(int)
                (IntPtr.Size == 8 ? sizeof(int) : 0) + //                          padding to 8 bytes
                IntPtr.Size + // IMDInternalImport       *m_pMDImport;
                IntPtr.Size + // IMetaDataImport2        *m_pImporter;
                IntPtr.Size + // IMetaDataEmit           *m_pEmitter;
                sizeof(int) + // Volatile<LONG>           m_refCount; // fuck C long
                0; // here is out bool m_isSystem

            if (IsDebugClr && IntPtr.Size == 8)
            {
                peAssemOffset += 2 * sizeof(int); // filled in padding
            }

            var m_isSystem = ((byte*)peAssembly) + peAssemOffset;
            *m_isSystem = 1;
        }

        protected override MethodInfo MakeCreateRuntimeMethodInfoStub(Type methodHandleInternal)
        {
            var methodHandleInternalConstructor = methodHandleInternal.GetConstructors((BindingFlags)(-1))[0];
            Helpers.DAssert(methodHandleInternalConstructor is not null);

            var runtimeMethodInfoStub = typeof(RuntimeMethodHandle).Assembly.GetType("System.RuntimeMethodInfoStub");
            Helpers.DAssert(runtimeMethodInfoStub is not null);
            var runtimeMethodInfoStubCtor = runtimeMethodInfoStub.GetConstructor([methodHandleInternal, typeof(object)]);
            Helpers.DAssert(runtimeMethodInfoStubCtor is not null);

            MethodInfo runtimeMethodInfoStubCtorWrapper;
            using (var dmd = new DynamicMethodDefinition(
                    "new RuntimeMethodInfoStub", runtimeMethodInfoStub, [typeof(IntPtr), typeof(object)]
                ))
            {
                var il = dmd.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Newobj, methodHandleInternalConstructor);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Newobj, runtimeMethodInfoStubCtor);
                il.Emit(OpCodes.Ret);

                runtimeMethodInfoStubCtorWrapper = dmd.Generate();
            }

            return runtimeMethodInfoStubCtorWrapper;
        }

        protected override MethodInfo GetOrCreateGetTypeFromHandleUnsafe()
        {
            var method = typeof(RuntimeTypeHandle)
                .GetMethod("GetRuntimeTypeFromHandleMaybeNull", (BindingFlags)(-1), null, [typeof(IntPtr)], null);
            Helpers.Assert(method is not null);
            return method;
        }
    }
}