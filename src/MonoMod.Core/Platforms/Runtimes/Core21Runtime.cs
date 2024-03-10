using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using static MonoMod.Core.Interop.CoreCLR;
using MC = Mono.Cecil;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core21Runtime : CoreBaseRuntime
    {

        public override RuntimeFeature Features => base.Features | RuntimeFeature.CompileMethodHook;

        public Core21Runtime(ISystem system) : base(system) { }

        // See FxCoreBaseRuntime. The location of the NoInlining flag in MDs has been *very* consistent over time.
        /*
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
            var m_wFlags = (ushort*) (((byte*) handle.Value) + offset);
            *m_wFlags |= 0x2000;
        }
        */

        private static JitHookHelpersHolder CreateJitHookHelpers(Core21Runtime self) => new(self);

        private readonly object sync = new();
        private JitHookHelpersHolder? lazyJitHookHelpers;
        protected unsafe JitHookHelpersHolder JitHookHelpers => Helpers.GetOrInitWithLock(ref lazyJitHookHelpers, sync, &CreateJitHookHelpers, this);

        // src/inc/corinfo.h line 216
        // 0ba106c8-81a0-407f-99a1-928448c1eb62
        private static readonly Guid JitVersionGuid = new Guid(
            0x0ba106c8,
            0x81a0,
            0x407f,
            0x99, 0xa1, 0x92, 0x84, 0x48, 0xc1, 0xeb, 0x62
        );

        protected virtual Guid ExpectedJitVersion => JitVersionGuid;

        protected virtual int VtableIndexICorJitCompilerGetVersionGuid => 4;
        protected virtual int VtableIndexICorJitCompilerCompileMethod => 0;

        protected virtual InvokeCompileMethodPtr InvokeCompileMethodPtr => V21.InvokeCompileMethodPtr;

        protected virtual Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<V21.CompileMethodDelegate>();

        protected static unsafe IntPtr* GetVTableEntry(IntPtr @object, int index)
            => (*(IntPtr**)@object) + index;
        protected static unsafe IntPtr ReadObjectVTable(IntPtr @object, int index)
            => *GetVTableEntry(@object, index);

        private unsafe void CheckVersionGuid(IntPtr jit)
        {
            var getVersionIdentPtr = (delegate* unmanaged[Thiscall]<IntPtr, Guid*, void>)ReadObjectVTable(jit, VtableIndexICorJitCompilerGetVersionGuid);
            Guid guid;
            getVersionIdentPtr(jit, &guid);
            Helpers.Assert(guid == ExpectedJitVersion,
                $"JIT version does not match expected JIT version! " +
                $"expected: {ExpectedJitVersion}, got: {guid}");
        }

        private Delegate? ourCompileMethod;
        private IDisposable? n2mHookHelper;
        private IDisposable? m2nHookHelper;

        protected unsafe override void InstallJitHook(IntPtr jit)
        {
            CheckVersionGuid(jit);

            // Get the real compile method vtable slot
            var compileMethodSlot = GetVTableEntry(jit, VtableIndexICorJitCompilerCompileMethod);
            var compileMethod = EHManagedToNative(*compileMethodSlot, out m2nHookHelper);

            // create our compileMethod delegate
            var ourCompileMethodDelegate = CastCompileHookToRealType(CreateCompileMethodDelegate(compileMethod));
            ourCompileMethod = ourCompileMethodDelegate; // stash it away so that it stays alive forever

            var ourCompileMethodPtr = EHNativeToManaged(Marshal.GetFunctionPointerForDelegate(ourCompileMethodDelegate), out n2mHookHelper);

            // invoke our CompileMethodPtr through ICMP to ensure that the JIT has compiled any needed thunks
            InvokeCompileMethodToPrepare(ourCompileMethodPtr);

            // and now we can install our method pointer as a JIT hook
            Span<byte> ptrData = stackalloc byte[sizeof(IntPtr)];
            MemoryMarshal.Write(ptrData, ref ourCompileMethodPtr);

            System.PatchData(PatchTargetKind.ReadOnly, (IntPtr)compileMethodSlot, ptrData, default);
        }

        protected unsafe virtual void InvokeCompileMethodToPrepare(IntPtr method)
        {
            V21.CORINFO_METHOD_INFO methodInfo;
            byte* nativeStart;
            uint nativeSize;
            InvokeCompileMethodPtr.InvokeCompileMethod(method, IntPtr.Zero, IntPtr.Zero, &methodInfo, 0, &nativeStart, &nativeSize);
        }

        // runtimes should override this if they need to significantly change the shape of CompileMethod
        protected unsafe virtual Delegate CreateCompileMethodDelegate(IntPtr compileMethod)
        {
            var del = new JitHookDelegateHolder(this, InvokeCompileMethodPtr, compileMethod).CompileMethodHook;
            return del;
        }

        private sealed class JitHookDelegateHolder
        {
            public readonly Core21Runtime Runtime;
            public readonly INativeExceptionHelper? NativeExceptionHelper;
            public readonly GetExceptionSlot? GetNativeExceptionSlot;
            public readonly JitHookHelpersHolder JitHookHelpers;
            public readonly InvokeCompileMethodPtr InvokeCompileMethodPtr;
            public readonly IntPtr CompileMethodPtr;

            public JitHookDelegateHolder(Core21Runtime runtime, InvokeCompileMethodPtr icmp, IntPtr compileMethod)
            {
                Runtime = runtime;
                NativeExceptionHelper = runtime.NativeExceptionHelper;
                JitHookHelpers = runtime.JitHookHelpers;
                InvokeCompileMethodPtr = icmp;
                CompileMethodPtr = compileMethod;

                // eagerly call ICMP to ensure that it's JITted before installing the hook
                unsafe
                {
                    V21.CORINFO_METHOD_INFO methodInfo;
                    byte* nativeStart;
                    uint nativeSize;
                    icmp.InvokeCompileMethod(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, &methodInfo, 0, &nativeStart, &nativeSize);
                }
                // and the same with MarshalEx.(Get/Set)LastPInvokeError
                MarshalEx.SetLastPInvokeError(MarshalEx.GetLastPInvokeError());
                // and the same for NativeExceptionHelper.NativeException { get; set; }
                if (NativeExceptionHelper is { } neh)
                {
                    GetNativeExceptionSlot = neh.GetExceptionSlot;
                    unsafe { _ = GetNativeExceptionSlot(); }
                }

                // ensure the static constructor has been called
                _ = hookEntrancy;
                hookEntrancy = 0;
            }

            [ThreadStatic]
            private static int hookEntrancy;

            [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                Justification = "We want to swallow exceptions here to prevent them from bubbling out of the JIT")]
            public unsafe CorJitResult CompileMethodHook(
                IntPtr jit, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                V21.CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** pNativeEntry,
                uint* pNativeSizeOfCode)
            {

                *pNativeEntry = null;
                *pNativeSizeOfCode = 0;

                if (jit == IntPtr.Zero)
                    return CorJitResult.CORJIT_OK;

                var lastError = MarshalEx.GetLastPInvokeError();
                nint nativeException = default;
                var pNEx = GetNativeExceptionSlot is { } getNex ? getNex() : null;
                hookEntrancy++;
                try
                {

                    /* We've silenced any exceptions thrown by this in the past but it turns out this can throw?!
                     * Let's hope that all runtimes we're hooking the JIT of know how to deal with this - oh wait, not all do!
                     * FIXME: Linux .NET Core pre-5.0 (and sometimes even 5.0) can die in real_compileMethod on invalid IL?!
                     * -ade
                     */
                    var result = InvokeCompileMethodPtr.InvokeCompileMethod(CompileMethodPtr,
                        jit, corJitInfo, methodInfo, flags, pNativeEntry, pNativeSizeOfCode);
                    // if a native exception was caught, return immediately and skip all of our normal processing
                    if (pNEx is not null && (nativeException = *pNEx) is not 0)
                    {
                        MMDbgLog.Warning($"Native exception caught in JIT by exception helper (ex: 0x{nativeException:x16})");
                        return result;
                    }

                    if (hookEntrancy == 1)
                    {
                        try
                        {
                            // This is the top level JIT entry point, do our custom stuff
                            RuntimeTypeHandle[]? genericClassArgs = null;
                            RuntimeTypeHandle[]? genericMethodArgs = null;

                            if (methodInfo->args.sigInst.classInst != null)
                            {
                                genericClassArgs = new RuntimeTypeHandle[methodInfo->args.sigInst.classInstCount];
                                for (var i = 0; i < genericClassArgs.Length; i++)
                                {
                                    genericClassArgs[i] = JitHookHelpers.GetTypeFromNativeHandle(methodInfo->args.sigInst.classInst[i]).TypeHandle;
                                }
                            }
                            if (methodInfo->args.sigInst.methInst != null)
                            {
                                genericMethodArgs = new RuntimeTypeHandle[methodInfo->args.sigInst.methInstCount];
                                for (var i = 0; i < genericMethodArgs.Length; i++)
                                {
                                    genericMethodArgs[i] = JitHookHelpers.GetTypeFromNativeHandle(methodInfo->args.sigInst.methInst[i]).TypeHandle;
                                }
                            }

                            var declaringType = JitHookHelpers.GetDeclaringTypeOfMethodHandle(methodInfo->ftn).TypeHandle;
                            var method = JitHookHelpers.CreateHandleForHandlePointer(methodInfo->ftn);

                            // codeStart and codeStartRw are the same because this runtime doesn't distinguish them at this point in the JIT
                            Runtime.OnMethodCompiledCore(declaringType, method, genericClassArgs, genericMethodArgs, (IntPtr)(*pNativeEntry), (IntPtr)(*pNativeEntry), *pNativeSizeOfCode);
                        }
                        catch
                        {
                            // eat the exception so we don't accidentally bubble up to native code
                        }
                    }

                    return result;
                }
                finally
                {
                    hookEntrancy--;
                    if (pNEx is not null)
                        *pNEx = nativeException;
                    MarshalEx.SetLastPInvokeError(lastError);
                }
            }
        }

        protected sealed class JitHookHelpersHolder
        {
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

            public RuntimeMethodHandle CreateHandleForHandlePointer(IntPtr handle)
                => CreateRuntimeMethodHandle(CreateRuntimeMethodInfoStub(handle, MethodHandle_GetLoaderAllocator(handle)));

            public JitHookHelpersHolder(Core21Runtime runtime)
            {

                const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

                // GetLoaderAllocator should always be present
                { // set up GetLoaderAllocator
                    var getLoaderAllocator = typeof(RuntimeMethodHandle).GetMethod("GetLoaderAllocator", StaticNonPublic);
                    Helpers.DAssert(getLoaderAllocator is not null);

                    MethodInfo invokeWrapper;
                    using (var dmd = new DynamicMethodDefinition(
                            "MethodHandle_GetLoaderAllocator", typeof(object), new Type[] { typeof(IntPtr) }
                        ))
                    {
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
                    var getTypeFromHandleUnsafe = GetOrCreateGetTypeFromHandleUnsafe(runtime);
                    GetTypeFromNativeHandle = getTypeFromHandleUnsafe.CreateDelegate<GetTypeFromNativeHandleD>();
                }

                { // set up GetDeclaringTypeOfMethodHandle
                    var methodHandleInternal = typeof(RuntimeMethodHandle).Assembly.GetType("System.RuntimeMethodHandleInternal");
                    Helpers.DAssert(methodHandleInternal is not null);
                    var getDeclaringType = typeof(RuntimeMethodHandle).GetMethod("GetDeclaringType", StaticNonPublic, null, new Type[] { methodHandleInternal }, null);
                    Helpers.DAssert(getDeclaringType is not null);

                    MethodInfo invokeWrapper;
                    using (var dmd = new DynamicMethodDefinition(
                            "GetDeclaringTypeOfMethodHandle", typeof(Type), new Type[] { typeof(IntPtr) }
                        ))
                    {
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
                    using (var dmd = new DynamicMethodDefinition(
                            "new RuntimeMethodInfoStub", runtimeMethodInfoStub, runtimeMethodInfoStubCtorArgs
                        ))
                    {
                        var il = dmd.GetILGenerator();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Newobj, runtimeMethodInfoStubCtor);
                        il.Emit(OpCodes.Ret);

                        runtimeMethodInfoStubCtorWrapper = dmd.Generate();
                    }

                    CreateRuntimeMethodInfoStub = runtimeMethodInfoStubCtorWrapper.CreateDelegate<CreateRuntimeMethodInfoStubD>();
                }

                { // set up CreateRuntimeMethodHandle
                    var ctor = typeof(RuntimeMethodHandle).GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).First();

                    MethodInfo ctorWrapper;
                    using (var dmd = new DynamicMethodDefinition(
                            "new RuntimeMethodHandle", typeof(RuntimeMethodHandle), new Type[] { typeof(object) }
                        ))
                    {
                        var il = dmd.GetILGenerator();
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
            private static MethodInfo GetOrCreateGetTypeFromHandleUnsafe(Core21Runtime runtime)
            {
                const string MethodName = "GetTypeFromHandleUnsafe";
                var method = typeof(Type).GetMethod(MethodName, (BindingFlags)(-1));

                if (method is not null)
                    return method;

                // the method not existing is far and away the more common case

                Assembly assembly;
                using (var module = ModuleDefinition.CreateModule(
                    "MonoMod.Core.Platforms.Runtimes.Core30Runtime+Helpers",
                    new ModuleParameters() { Kind = ModuleKind.Dll }
                ))
                {
                    var sysType = new TypeDefinition(
                        "System",
                        "Type",
                        MC.TypeAttributes.Public | MC.TypeAttributes.Abstract
                    )
                    {
                        BaseType = module.TypeSystem.Object
                    };
                    module.Types.Add(sysType);

                    var targetMethod = new MethodDefinition(
                        MethodName,
                        MC.MethodAttributes.Static | MC.MethodAttributes.Public,
                        module.ImportReference(typeof(Type))
                    )
                    {
                        IsInternalCall = true
                    };
                    targetMethod.Parameters.Add(new(module.ImportReference(typeof(IntPtr))));
                    sysType.Methods.Add(targetMethod);

                    assembly = ReflectionHelper.Load(module);
                }

                runtime.MakeAssemblySystemAssembly(assembly);

                var type = assembly.GetType("System.Type");
                Helpers.DAssert(type is not null);
                method = type.GetMethod(MethodName, (BindingFlags)(-1));
                Helpers.DAssert(method is not null);
                return method;
            }
        }

        private static readonly FieldInfo RuntimeAssemblyPtrField = Type.GetType("System.Reflection.RuntimeAssembly")!
            .GetField("m_assembly", BindingFlags.Instance | BindingFlags.NonPublic)!;

        protected virtual unsafe void MakeAssemblySystemAssembly(Assembly assembly)
        {
            // RuntimeAssembly.m_assembly is a DomainAssembly*,
            // which contains an Assembly*,
            // which contains a PEAssembly*,
            // which is a subclass of PEFile
            // which has a `flags` field, with bit 0x01 representing 'system'

            const int PEFILE_SYSTEM = 0x01;

            var domAssembly = (IntPtr)RuntimeAssemblyPtrField.GetValue(assembly)!;

            // DomainAssembly in src/coreclr/src/vm/domainfile.h
            var domOffset =
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
                              // #ifdef FEATURE_PREJIT Volatile<DomainFile*> m_pNextDomainFileWithNativeImage;
                              // DomainAssembly
                IntPtr.Size + // LOADERHANDLE                            m_hExposedAssemblyObject;
                0; // here is our Assembly*

            if (IntPtr.Size == 8)
            {
                domOffset +=
                    sizeof(int); // padding to align the next TADDR (which is a void*) (m_hExposedModuleObject)
            }

            var pAssembly = *(IntPtr*)(((byte*)domAssembly) + domOffset);

            // Assembly in src/coreclr/src/vm/assembly.hpp
            var pAssemOffset =
                IntPtr.Size + // PTR_BaseDomain        m_pDomain;
                IntPtr.Size + // PTR_ClassLoader       m_pClassLoader;
                IntPtr.Size + // PTR_MethodDesc        m_pEntryPoint;
                IntPtr.Size + // PTR_Module            m_pManifest;
                0; // here is out PEAssembly* (m_pManifestFile)

            var peAssembly = *(IntPtr*)(((byte*)pAssembly) + pAssemOffset);

            // PEAssembly in src/coreclr/src/vm/pefile.h
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
                IntPtr.Size + // PTR_PEImage              m_identity;
                IntPtr.Size + // PTR_PEImage              m_openedILimage;
                sizeof(int) + // BOOL                     m_MDImportIsRW_Debugger_Use_Only; // i'm pretty sure that these bools are sizeof(int)
                sizeof(int) + // Volatile<BOOL>           m_bHasPersistentMDImport;         // but they might not be, and it might vary (that would be a pain in the ass)
                IntPtr.Size + // IMDInternalImport       *m_pMDImport;
                IntPtr.Size + // IMetaDataImport2        *m_pImporter;
                IntPtr.Size + // IMetaDataEmit           *m_pEmitter;
                IntPtr.Size + // SimpleRWLock            *m_pMetadataLock;
                sizeof(int) + // Volatile<LONG>           m_refCount; // fuck C long
                0; // here is out int (flags)

            if (IsDebugClr && IntPtr.Size == 8)
            {
                peAssemOffset += 2 * sizeof(int); // filled in padding
            }

            var flags = (int*)(((byte*)peAssembly) + peAssemOffset);
            *flags |= PEFILE_SYSTEM;
        }
    }
}

