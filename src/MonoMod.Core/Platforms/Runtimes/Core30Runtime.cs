using Mono.Cecil;
using MonoMod.Core.Utils;
using MonoMod.Utils;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MC = Mono.Cecil;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class Core30Runtime : CoreBaseRuntime {

        public override RuntimeFeature Features => base.Features | RuntimeFeature.DisableInlining;

        public unsafe override void DisableInlining(MethodBase method) {
            // https://github.com/dotnet/runtime/blob/89965be3ad2be404dc82bd9e688d5dd2a04bcb5f/src/coreclr/src/vm/method.hpp#L178
            // mdcNotInline = 0x2000
            // References to RuntimeMethodHandle (CORINFO_METHOD_HANDLE) pointing to MethodDesc
            // can be traced as far back as https://ntcore.com/files/netint_injection.htm

            var handle = GetMethodHandle(method);

            const int offset =
                2 // UINT16 m_wFlags3AndTokenRemainder
              + 1 // BYTE m_chunkIndex
              + 1 // BYTE m_chunkIndex
              + 2 // WORD m_wSlotNumber
              ;
            ushort* m_wFlags = (ushort*) (((byte*) handle.Value) + offset);
            *m_wFlags |= 0x2000;
        }

        private static JitHookHelpersHolder CreateJitHookHelpers(Core30Runtime self) => new(self);

        private readonly object sync = new();
        private JitHookHelpersHolder? lazyJitHookHelpers;
        protected unsafe JitHookHelpersHolder JitHookHelpers => Helpers.GetOrInitWithLock(ref lazyJitHookHelpers, sync, &CreateJitHookHelpers, this);

        // d609bed1-7831-49fc-bd49-b6f054dd4d46
        private static readonly Guid JitVersionGuid = new Guid(
            0xd609bed1, 0x7831, 0x49fc,
            0xbd, 0x49, 0xb6, 0xf0, 0x54, 0xdd, 0x4d, 0x46);

        protected virtual Guid ExpectedJitVersion => JitVersionGuid;

        protected virtual int VtableGetVersionIdentifierIndex => 4;

        protected static unsafe IntPtr* GetVTableEntry(IntPtr @object, int index)
            => (*(IntPtr**) @object) + index;
        protected static unsafe IntPtr ReadObjectVTable(IntPtr @object, int index)
            => *GetVTableEntry(@object, index);

        private unsafe void CheckVersionGuid(IntPtr jit) {
            var getVersionIdentPtr = (delegate* unmanaged[Thiscall]<IntPtr, out Guid, void>) ReadObjectVTable(jit, VtableGetVersionIdentifierIndex);
            getVersionIdentPtr(jit, out var guid);
            Helpers.Assert(guid == ExpectedJitVersion,
                $"JIT version does not match expected JIT version! " +
                $"expected: {ExpectedJitVersion}, got: {guid}");
        }

        protected override void InstallJitHook(IntPtr jit) {
            CheckVersionGuid(jit);

            // TODO: implement
        }

        protected sealed class JitHookHelpersHolder {
            public delegate object MethodHandle_GetLoaderAllocatorD(IntPtr methodHandle);
            public delegate object CreateRuntimeMethodInfoStubD(IntPtr methodHandle, object loaderAllocator);
            public delegate RuntimeMethodHandle CreateRuntimeMethodHandleD(object runtimeMethodInfo);
            public delegate Type GetDeclaringTypeOfMethodHandleD(IntPtr methodHandle);
            public delegate Type GetTypeFromNativeHandleD(IntPtr handle);

            public readonly MethodHandle_GetLoaderAllocatorD MethodHandle_GetLoaderAllocator;
            public readonly CreateRuntimeMethodInfoStubD CreateRuntimeMethodInfoStub;
            public readonly CreateRuntimeMethodHandleD CreateRuntimeMethodHandle;
            public readonly GetDeclaringTypeOfMethodHandleD GetDeclaringTypeOfMethodHandle;
            public readonly GetTypeFromNativeHandleD GetTypeFromNativeHandle;

            public JitHookHelpersHolder(Core30Runtime runtime) {

                const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

                // GetLoaderAllocator should always be present
                { // set up GetLoaderAllocator
                    var getLoaderAllocator = typeof(RuntimeMethodHandle).GetMethod("GetLoaderAllocator", StaticNonPublic);
                    Helpers.DAssert(getLoaderAllocator is not null);

                    MethodInfo invokeWrapper;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                            "MethodHandle_GetLoaderAllocator", typeof(object), new Type[] { typeof(IntPtr) }
                        )) {
                        var il = dmd.GetILGenerator();
                        var paramType = getLoaderAllocator.GetParameters().First().ParameterType;
                        il.Emit(OpCodes.Ldarga_S, 0);
                        // Unsafe.As shouldn't be needed
                        //il.Emit(OpCodes.Call, Unsafe_As.MakeGenericMethod(typeof(IntPtr), paramType));
                        il.Emit(OpCodes.Ldobj, paramType);
                        il.Emit(OpCodes.Call, getLoaderAllocator);
                        il.Emit(OpCodes.Ret);

                        invokeWrapper = dmd.Generate();
                    }

                    MethodHandle_GetLoaderAllocator = invokeWrapper.CreateDelegate<MethodHandle_GetLoaderAllocatorD>();
                }

                { // set up GetTypeFromNativeHandle
                    MethodInfo getTypeFromHandleUnsafe = GetOrCreateGetTypeFromHandleUnsafe(runtime);
                    GetTypeFromNativeHandle = getTypeFromHandleUnsafe.CreateDelegate<GetTypeFromNativeHandleD>();
                }

                { // set up GetDeclaringTypeOfMethodHandle
                    var methodHandleInternal = typeof(RuntimeMethodHandle).Assembly.GetType("System.RuntimeMethodHandleInternal");
                    Helpers.DAssert(methodHandleInternal is not null);
                    var getDeclaringType = typeof(RuntimeMethodHandle).GetMethod("GetDeclaringType", StaticNonPublic, null, new Type[] { methodHandleInternal }, null);
                    Helpers.DAssert(getDeclaringType is not null);

                    MethodInfo invokeWrapper;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                            "GetDeclaringTypeOfMethodHandle", typeof(Type), new Type[] { typeof(IntPtr) }
                        )) {
                        var il = dmd.GetILGenerator();
                        il.Emit(OpCodes.Ldarga_S, 0);
                        // Unsafe.As shouldn't be needed
                        //il.Emit(OpCodes.Call, Unsafe_As.MakeGenericMethod(typeof(IntPtr), methodHandleInternal));
                        il.Emit(OpCodes.Ldobj, methodHandleInternal);
                        il.Emit(OpCodes.Call, getDeclaringType);
                        il.Emit(OpCodes.Ret);

                        invokeWrapper = dmd.Generate();
                    }

                    GetDeclaringTypeOfMethodHandle = invokeWrapper.CreateDelegate<GetDeclaringTypeOfMethodHandleD>();
                }

                { // set up CreateRuntimeMethodInfoStub
                    var runtimeMethodInfoStubCtorArgs = new[] { typeof(IntPtr), typeof(object) };
                    var runtimeMethodInfoStub = typeof(RuntimeMethodHandle).Assembly.GetType("System.RuntimeMethodInfoStub");
                    Helpers.DAssert(runtimeMethodInfoStub is not null);
                    var runtimeMethodInfoStubCtor = runtimeMethodInfoStub.GetConstructor(runtimeMethodInfoStubCtorArgs);
                    Helpers.DAssert(runtimeMethodInfoStubCtor is not null);

                    MethodInfo runtimeMethodInfoStubCtorWrapper;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                            "new RuntimeMethodInfoStub", runtimeMethodInfoStub, runtimeMethodInfoStubCtorArgs
                        )) {
                        ILGenerator il = dmd.GetILGenerator();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Newobj, runtimeMethodInfoStubCtor);
                        il.Emit(OpCodes.Ret);

                        runtimeMethodInfoStubCtorWrapper = dmd.Generate();
                    }

                    CreateRuntimeMethodInfoStub = runtimeMethodInfoStubCtorWrapper.CreateDelegate<CreateRuntimeMethodInfoStubD>();
                }

                { // set up CreateRuntimeMethodHandle
                    ConstructorInfo ctor = typeof(RuntimeMethodHandle).GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).First();

                    MethodInfo ctorWrapper;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                            "new RuntimeMethodHandle", typeof(RuntimeMethodHandle), new Type[] { typeof(object) }
                        )) {
                        ILGenerator il = dmd.GetILGenerator();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Newobj, ctor);
                        il.Emit(OpCodes.Ret);

                        ctorWrapper = dmd.Generate();
                    }

                    CreateRuntimeMethodHandle = ctorWrapper.CreateDelegate<CreateRuntimeMethodHandleD>();
                }
            }

            /// <summary>
            /// This method gets or creates the internal call for Type.GetTypeFromHandleUnsafe.
            /// The internal call always exists, but the managed method doesn't in some cases.
            /// </summary>
            /// <returns></returns>
            private static MethodInfo GetOrCreateGetTypeFromHandleUnsafe(Core30Runtime runtime) {
                const string MethodName = "GetTypeFromHandleUnsafe";
                var method = typeof(Type).GetMethod(MethodName, (BindingFlags) (-1));

                if (method is not null)
                    return method;

                // the method not existing is far and away the more common case

                Assembly assembly;
                using (var module = ModuleDefinition.CreateModule(
                    "MonoMod.Core.Platforms.Runtimes.Core30Runtime+Helpers",
                    new ModuleParameters() { Kind = ModuleKind.Dll }
                )) {
                    var sysType = new TypeDefinition(
                        "System",
                        "Type",
                        MC.TypeAttributes.Public | MC.TypeAttributes.Abstract
                    ) {
                        BaseType = module.TypeSystem.Object
                    };
                    module.Types.Add(sysType);

                    var targetMethod = new MethodDefinition(
                        MethodName,
                        MC.MethodAttributes.Static | MC.MethodAttributes.Public,
                        module.ImportReference(typeof(Type))
                    ) {
                        IsInternalCall = true
                    };
                    targetMethod.Parameters.Add(new(module.ImportReference(typeof(IntPtr))));
                    sysType.Methods.Add(targetMethod);

                    assembly = ReflectionHelper.Load(module);
                }

                runtime.MakeAssemblySystemAssembly(assembly);

                var type = assembly.GetType("System.Type");
                Helpers.DAssert(type is not null);
                method = type.GetMethod(MethodName, (BindingFlags) (-1));
                Helpers.DAssert(method is not null);
                return method;
            }
        }

        private static readonly FieldInfo RuntimeAssemblyPtrField = Type.GetType("System.Reflection.RuntimeAssembly")!
            .GetField("m_assembly", BindingFlags.Instance | BindingFlags.NonPublic)!;
        protected virtual unsafe void MakeAssemblySystemAssembly(Assembly assembly) {


            // RuntimeAssembly.m_assembly is a DomainAssembly*,
            // which contains an Assembly*,
            // which contains a PEAssembly*,
            // which is a subclass of PEFile
            // which has a `flags` field, with bit 0x01 representing 'system'

            const int PEFILE_SYSTEM = 0x01;

            IntPtr domAssembly = (IntPtr) RuntimeAssemblyPtrField.GetValue(assembly)!;

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

            if (IntPtr.Size == 8) {
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
                +0; // here is out int (flags)

            int* flags = (int*) (((byte*) peAssembly) + peAssemOffset);
            *flags |= PEFILE_SYSTEM;
        }
    }
}
