using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class Core70Runtime : Core60Runtime {

        private readonly IArchitecture arch;

        public Core70Runtime(ISystem system, IArchitecture arch) : base(system) {
            this.arch = arch;
        }

        // src/coreclr/inc/jiteeversionguid.h line 46
        // 6be47e5d-a92b-4d16-9280-f63df646ada4
        private static readonly Guid JitVersionGuid = new Guid(
            0x6be47e5d,
            0xa92b,
            0x4d16,
            0x92, 0x80, 0xf6, 0x3d, 0xf6, 0x46, 0xad, 0xa4
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        // As a part of .NET 7's W^X support, the JIT doesn't actually write its code directly to the output.
        // Instead it's copied in to the location that it passes as an out parameter *after* the JIT returns. 
        // Therefore, in order to have our patches applied correctly, we need to poke into the CEEInto parameter
        // to find the address of the RW code and write to that instead.

        // It may actually be easier to wrap the CEEInfo we pass to the actual JIT so we can intercept allocMem
        // and get the actual writable regions form that call.

        // class ICorDynamicInfo : public ICorStaticInfo
        // class ICorJitInfo : public ICorDynamicInfo
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        protected unsafe struct ICorJitInfoWrapper {
            public IntPtr Vtbl;
            public IntPtr Wrapped;

            public const int HotCodeRW = 0;
            public const int ColdCodeRW = 1;
            // the other 2-6 are left unused

            private const int DataQWords = 4;
            private fixed ulong data[DataQWords];

            public ref IntPtr this[int index] {
                get {
                    Helpers.DAssert(index < (DataQWords * sizeof(ulong)) / IntPtr.Size);
                    return ref Unsafe.Add(ref Unsafe.As<ulong, IntPtr>(ref data[0]), index);
                }
            }

            // we need to copy the full vtable, and these are the slots:

            // src/coreclr/inc/corinfo.h
            // class ICorStaticInfo
            //  0: bool isIntrinsic(MethodDesc*)
            //  1: uint32_t getMethodAttribs(MethodDesc*)
            //  2: void setMethodAttribs(MethodDesc*, CorInfoMethodRuntimeFlags)
            //  3: void getMethodSig(MethodDesc*, CORINFO_SIG_INFO*, CORINFO_CLASS_HANDLE = null)
            //  4: bool getMethodInfo(MethodDesc*, CORINFO_METHOD_INFO*)
            //  5: CorInfoInline canInline(MethodDesc* caller, MethodDesc* callee)
            //  6: void beginInlining(MethodDesc*, MethodDesc*)
            //  7: void reportInliningDecision(MethodDesc*, MethodDesc*, CorInfoInline, char const*)
            //  8: bool canTailCall(MethodDesc*, MethodDesc*, MethodDesc*, bool)
            //  9: void reportTailCallDecision(MethodDesc*, MethodDesc*, bool, CorInfoTailCall, char const*)
            //  A: void getEHInfo(MethodDesc*, unsigned, CORINFO_EH_CLAUSE*)
            //  B: CORINFO_CLASS_HANDLE getMethodClass(MethodDesc*)
            //  C: CORINFO_MODULE_HANDLE getMethodModule(MethodDesc*)
            //  D: void getMethodVTableOffset(MethodDesc*, unsigned*, unsigned*, bool*)
            //  E: bool resolveVirtualMethod(CORINFO_DEVIRTUALIZATION_INFO*)
            //  F: MethodDesc* getUnboxedEntry(MethodDesc*, bool*)
            // 10: CORINFO_CLASS_HANDLE getDefaultComparerClass(CORINFO_CLASS_HANDLE)
            // 11: CORINFO_CLASS_HANDLE getDefaultEqualityComparerClass(CORINFO_CLASS_HANDLE)
            // 12: void expandRawHandleIntrinisc(CORINFO_RESOLVED_TOKEN*, CORINFO_GENERICHANDLE_RESULT*)
            // 13: bool isIntrinsicType(CORINFO_CLASS_HANDLE)
            // 14: CorInfoCallConvExtension getUnmanagedCallConv(MethodDesc*, CORINFO_SIG_INFO*, bool*)
            // 15: bool pInvokeMarshallingRequired(MethodDesc*, CORINFO_SIG_INFO*)
            // 16: bool satisfiesMethodConstraints((CORINFO_CLASS_HANDLE, MethodDesc*)
            // 17: bool isCompatibleDelegate(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE, MethodDesc*, CORINFO_CLASS_HANDLE, bool*)
            // 18: void methodMustBeLoadedBeforeCodeIsRun(MethodDesc*)
            // 19: MethodDesc* mapMethodDeclToMethodIMpl(MethodDesc*)
            // 1A: void getGSCookie(GSCookie*, GSCookie**)
            // 1B: void setPatchpointInfo(PatchpointInfo*)
            // 1C: PatchpointInfo* getOSRInfo(unsigned*)
            // 1D: void resolveToken(CORINFO_RESOLVED_TOKEN*)
            // 1E: bool tryResolveToken(CORINFO_RESOLVED_TOKEN*)
            // 1F: void findSig(CORINFO_MODULE_HANDLE, unsigned, CORINFO_CONTEXT_HANDLE, CORINFO_SIG_INFO*)
            // 20: void findCallSiteSig(CORINFO_MODULE_HANDLE, unsigned, CORINFO_CONTEXT_HANDLE, CORINFO_SIG_INFO*)
            // 21: CORINFO_CLASS_HANDLE getTokenTypeAsHandle(CORINFO_RESOLVED_TOKEN*)
            // 22: bool isValidToken(CORINFO_MODULE_HANDLE, unsigned)
            // 23: bool isValidStringRef(CORINFO_MODULE_HANDLE, unsigned)
            // 24: int getStringLiteral(CORINFO_MODULE_HANDLE, unsigned, char16_t*, int)
            // 25: CorInfoType asCorInfoType(CORINFO_CLASS_HANDLE)
            // 26: char const* getClassName(CORINFO_CLASS_HANDLE)
            // 27: char const* getClassNameFromMetadata(CORINFO_CLASS_HANDLE, char const**)
            // 28: CORINFO_CLASS_HANDLE getTypeInstantiationArgument(CORINFO_CLASS_HANDLE, unsigned)
            // 29: int appendClassName(char16_t**, int*, CORINFO_CLASS_HANDLE, bool, bool, bool)
            // 2A: bool isValueClass(CORINFO_CLASS_HANDLE)
            // 2B: CorInfoinlineTypeCheck canInlineTypeCheck(CORINFO_CLASS_HANDLE, CorInfoInlineTypeCheckSource)
            // 2C: uint32_t getClassAttribs(CORINFO_CLASS_HANDLE)
            // 2D: CORINFO_MODULE_HANDLE getClassModule(CORINFO_CLASS_HANDLE)
            // 2E: CORINFO_ASSEMBLY_HANDLE getModuleAssembly(CORINFO_MODULE_HANDLE)
            // 2F: char const* getAssemblyName()CORINFO_ASSEMBLY_HANDLE)
            // 30: void* LongLifetimeMalloc(size_t)
            // 31: void LogLifetimemFree(void*)
            // 32: size_t getClassModuleIdForStatics(CORINFO_CLASS_HANDLE, CORINFO_MODULE_HANDLE*, void**)
            // 33: unsigned getClassSize(CORINFO_CLASS_HANDLE)
            // 34: unsigned getHeapClassSize(CORINFO_CLASS_HANDLE)
            // 35: bool canAllocateOnStaci(CORINFO_CLASS_HANDLE)
            // 36: unsigned getClassAlignmentRequirement(CORINFO_CLASS_HANDLE, bool=false)
            // 37: unsigned getClassGClayout(CORINFO_CLASS_HANDLE, uint8_t*)
            // 38: unsigned getClassNumInstanceFields(CORINFO_CLASS_HANDLE)
            // 39: CORINFO_FIELD_HANDLE getFieldInClass(CORINFO_CLASS_HANDLE, int32_t)
            // 3A: bool checkMethodModifier(MethodTable*, char const*, bool)
            // 3B: CorInfoHelpFunc getNewHelper(CORINFO_RESOLVED_TOKEN*, MethodTable*, bool*)
            // 3C: CorInfoHelpFunc getNewArrHelper(CORINFO_CLASS_HANDLE)
            // 3D: CorInfoHelpFunc getCastingHelper(CORINFO_RESOLVED_TOKEN*, bool)
            // 3E: CorInfoHelpFunc getCharedCCtorHelper(CORINFO_CLASS_HANDLE)
            // 3F: CORINFO_CLASS_HANDLE getTypeForBox(CORINFO_CLASS_HANDLE)
            // 40: CorInfoHelpFunc getBoxHelper(CORINFO_CLASS_HANDLE)
            // 41: CorInfoHelpFunc getUnBoxHelper(CORINFO_CLASS_HANDLE)
            // 42: bool getReadyToRunHelper(CORINFO_RESOLVED_TOKEN*, CORINFO_LOOKUP_KIND*, CorInfoHelpFunc, CORINFO_CONST_LOOKUP*)
            // 43: void getReadyToRunDelegateCtorHelper(CORINFO_RESOLVED_TOKEN*, mdToken, CORINFO_CLASS_HANDLE, CORINFO_LOOKUP*)
            // 44: char const* getHelperName(CorInfoHelpFunc)
            // 45: CorInfoInitClassResult initClass(CORINFO_FIELD_HANDLE, CORINFO_METHOD_HANDLE, CORINFO_CONTEXT_HANDLE)
            // 46: void classMustBeLoadedBeforeCodeIsRun(CORINFO_CLASS_HANDLE)
            // 47: CORINFO_CLASS_HANDLE getBuiltinClass(CorInfoClassId)
            // 48: CorInfoType getTypeForPrimitiveValueClass(CORINFO_CLASS_HANDLE)
            // 49: CorInfoType getTypeForPrimitiveNumericClass(CORINFO_CLASS_HANDLE)
            // 4A: bool canCast(CORINFO_CLASS_HANDLE)
            // 4B: bool areTypesEquivalent(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
            // 4C: TypeCompareState compareTypesForCast(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
            // 4D: TypeCompareState compareTypesForEquality(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE
            // 4E: CORINFO_CLASS_HANDLE mergeClasses(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
            // 4F: bool isMoreSpecificType(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
            // 50: CORINFO_CLASS_HANDLE getParentType(CORINFO_CLASS_HANDLE)
            // 51: CorInfoType getChildType(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE*)
            // 52: bool satisfiesClassConstraints(CORINFO_CLASS_HANDLE)
            // 53: bool isSDArray(CORINFO_CLASS_HANDLE)
            // 54: unsigned getArrayRank(CORINFO_CLASS_HANDLE)
            // 55: CorInfoArrayIntrinsic getArrayIntrinsicID(CORINFO_METHOD_HANDLE)
            // 56: void* getArrayInitializationData(CORINFO_FIELD_HANDLE, uint32_t)
            // 57: CorInfoIsAccessAllowedResult canAccessClas(CORINFO_RESOLVED_TOKEN*, CORINFO_METHOD_HANDLE, CORINFO_HELPER_DESC*)
            // 58: char const* getFieldName(CORINFO_FIELD_HANDLE, char const**)
            // 59: CORINFO_CLASS_HANDLE getFieldClass(CORINFO_FIELD_HANDLE)
            // 5A: CorInfoType getFieldType(CORINFO_FIELD_HANDLE, CORINFO_CLASS_HANDLE*, CORINFO_CLASS_HANDLE)
            // 5B: unsigned getFieldOffset(CORINFO_FIELD_HANDLE)
            // 5C: void getFieldInfo(CORINFO_RESOLVED_TOKEN*, CORINFO_METHOD_HANDLE, CORINFO_ACCESS_FLAGS, CORINFO_FIELD_INFO)
            // 5D: bool isFieldStatic(CORINFO_FIELD_HANDLE)
            // 5E: void getBoundaries(CORINFO_METHOD_HANDLE, unsigned int*, uint32_t**, ICorDebugInfo::BoundaryTypes*)
            // 5F: void setBoundaries(CORINFO_METHOD_HANDLE, uint32_t, ICorDebugInfo::OffsetMapping*)
            // 60: void getVars(CORINFO_METHOD_HANDLE, uint32_t*, ICorDebugInfo::ILVarInfo**, bool*)
            // 61: void setVars(CORINFO_METHOD_HANDLE, uint32_t, ICorDebugINfo:;NativeCatInfo*)
            // 62: void reportRichMappings(ICorDebugInfo::InlineTreeNode*, uint32_t, ICodDebugInfo::RichOffsetMapping*, uint32_t)
            // 63: void* allocateArray(size_t)
            // 64: void freeArray(void*)
            // 65: CORINFO_ARG_LIST_HANDLE getArgNext(CORINFO_ARG_LIST_HANDLE)
            // 66: CorInfoTypeWithMod getArgType(CORINFO_SIG_INFO*, CORINFO_ARG_LIST_HANDLE, CORINFO_CLASS_HANDLE*)
            // 67: int getExactClasses(CORINFO_CLASS_HANDLE, int, CORINFO_CLASS_HANDLE*)
            // 68: CORINFO_CLASS_HANDLE getArgClass(CORINFO_SIG_INFO*, CORINFO_ARG_LIST_HANDLE)
            // 69: CorInfoHFAElemType getHFAType(CORINFO_CLASS_HANDLE)
            // 6A: JITINTERFACE_HRESULT GetErrorHRESULT(struct _EXCEPTION_POINTERS*)
            // 6B: uint32_t GetErrorMessage(char16_t*, uint32_t)
            // 6C: int FilterException(struct _EXCEPTION_POINTERS*)
            // 6D: void ThrowExceptionForJitResult(JITINTERFACE_HRESULT)
            // 6E: void ThrowExceptionForHelper(CORINFO_HELPER_DESC const*)
            // 6F: bool runWithErrorTrap(errorTrapFunction, void*)
            // 70: bool runWithSPMIErrorTrap(errorTrapFunction, void*)
            // 71: void getEEInfo(CORINFO_EE_INFO*)
            // 72: char16_t const* getJitTimeLogFilename()
            // 73: mdMethodDef getMethodDefFromMethod(CORINFO_METHOD_HANDLE)
            // 74: char const* getMethodName(CORINFO_METHOD_HANDLE, char const**)
            // 75: char const* getMethodNameFromMetadata(CORINFO_METHOD_HANDLE, char const**, char const**, char const**)
            // 76: unsigned getMethodHash(CORINFO_METHOD_HANDLE)
            // 77: size_t findNameOFToken(CORINFO_MODULE_HANDLE, mdToken, char*, size_t)
            // 78: bool getSystemVAmd64PassStructInRegisterDescriptor(CORINFO_CLASS_HANDLE, ...*)
            // 79: uint32_t getLoongArch64PassStructInRegisterFlags(CORINFO_CLASS_HANDLE)

            // src/coreclr/inc/corinfo.h
            // class ICorDynamicInfo : public ICorStaticInfo
            // 7A: uint32_t getThreadTLSIndex(void**=null)
            // 7B: void const* getInlinedCallFrameVptr(void**=null)
            // 7C: int32_t* getAddrOfCaptureThreadGlobal(void**=null)
            // 7D: void* getHelperFtn(CorInfoHelpFunc,void**=null)
            // 7E: void getFunctionEntryPoint(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP*, CORINFO_ACCESS_FLAGS=ANY)
            // 7F: void getFunctionFixedEntryPoint(CORINFO_METHOD_HANDLE, bool, CORINFO_CONST_LOOKUP*)
            // 80: void* getMethodSync(CORINFO_METHOD_HANDLE, void**=null)
            // 81: CorInfoHelpFunc getLazyStringLiteralHelper(CORINFO_MODULE_HANDLE)
            // 82: CORINFO_MODULE_HANDLE embedModuleHandle(CORINFO_MODULE_HANDLE, void**=null)
            // 83: CORINFO_CLASS_HANDLE embedClassHandle(CORINFO_CLASS_HANDLE, void**=null)
            // 84: CORINFO_METHOD_HANDLE embedMethodHandle(CORINFO_METHOD_HANDLE, void**=null)
            // 85: CORINFO_FIELD_HANDLE embedFieldHandle(CORINFO_FIELD_HANDLE, void**=null)
            // 86: void embedGenericHandle(CORINFO_RESOLVED_TOKEN*, bool, CORINFO_GENERICHANDLE_RESULT*)
            // 87: void getLocationOfThisType(CORINFO_METHOD_HANDLE, CORINFO_LOOKUP_KIND*)
            // 88: void getAddressOfPInvokeTarget(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP*)
            // 89: void* GetCookieForPINvokeCalliSig(CORINFO_SIG_INFO*, void**=null)
            // 8A: bool canGetCookieForPInvokeCalliSig(CORINFO_SIG_INFO*)
            // 8B: CORINFO_JUST_MY_CODE_HANDLE getJustMyCodeHandle(CORINFO_METHOD_HANDLE, CORINFO_JUST_MY_CODE_HANDLE**=null)
            // 8C: void GetProfilingHandle(bool*, void**, bool*)
            // 8D: void getCallInfo(CORINFO_RESOLVED_TOKEN*, CORINFO_RESOLVED_TOKEN*, CORINFO_METHOD_HANDLE, CORINFO_CALLINFO_FLAGS, CORINFO_CALL_INFO*)
            // 8E: bool canAccessFamily(CORINFO_METHOD_HANDLE, CORINFO_CLASS_HANDLE)
            // 8F: bool isRIDClassDomainID(CORINFO_CLASS_HANDLE)
            // 90: unsigned getClassDomainID(CORINFO_CLASS_HANDLE, void**=null)
            // 91: void* getFieldAddress(CORINFO_FIELD_HANDLE, void**=null)
            // 92: CORINFO_CLASS_HANDLE getStaticFieldCurrentClass(CORINFO_FIELD_HANDLE, bool*=null)
            // 93: CORINFO_VARARGS_HANDLE getVarArgsHandle(CORINFO_SIG_INFO*, void**=null)
            // 94: bool canGetVarArgsHandle(CORINFO_SIG_INFO*)
            // 95: InfoAccessType constructStringLiteral(CORINFO_MODULE_HANDLE, mdToken, void**)
            // 97: InfoAccessType emptyStringLiteral(void**)
            // 98: uint32_t getFieldThreadLocalStoreID(CORINFO_FIELD_HANDLE, void**=null)
            // 99: void addActiveDependency(CORINFO_MODULE_HANDLE, CORINFO_MODULE_HANDLE)
            // 9A: CORINFO_METHOD_HANDLE GetDelegateCtor(CORINFO_METHOD_HANDLE< CORINFO_CLASS_HANDLE, CORINFO_METHOD_HANDLE, DelegateCtorArgs*)
            // 9B: void MethodCompileComplete(CORINFO_METHOD_HANDLE)
            // 9C: bool getTailCallHelpers(CORINFO_RESOLVED_TOKEN*, CORINFO_SIG_INFO*, CORINFO_GET_TAILCALL_HELPERS_FLAGS, CORINFO_TAILCALL_HELPERS*)
            // 9D: bool convertPInvokeCalliToCall(CORINFO_RESOLVED_TOKEN*, bool)
            // 9E: bool notifyInstructionSetUsage(CORINFO_InstructionSet, bool)
            // 9F: void updateEntryPOintForTailCall(CORINFO_CONST_LOOKUP*)

            // src/coreclr/inc/corjit.h
            // class ICorJitInfo : public ICorDynamicInfo
            // A0: void allocMem(AllocMemArgs*)
            public const int AllocMemIndex = 0xA0;
            // A1: void reserveUnwindInfo(bool, bool, uint33_t)
            // A2: void allocUnwindInfo(uint8_t*, uint8_t*, uint32_t, uint32_t, uint32_t, uint8_t*, CorJitFuncKind)
            // A3: void* allocGCInfo(size_t)
            // A4: void setEHcount(unsigned)
            // A5: void setEHinfo(unsigned, CORINFO_EH_CLAUSE const*)
            // A6: bool logMsg(unsigned, char const*, va_list)
            // A7: int doAssert(char const*, int, char const*)
            // A8: void reportFatalError(CorJitResult)
            // A9: JITINTERFACE_HRESULT getPgoInstrumentationResults(CORINFO_METHOD_HANDLE, PgoInstrumentationSchema**, uint32_t*, uint8_t**, PgoSource*)
            // AA: JITINTERFACE_HRESULT allocPgoInstrumentationBySchema(CORINFO_METHOD_HANDLE, PgoInstrumentationSchema*)
            // AB: void recordCallSite(uint32_t, CORINFO_SIG_INFO*, CORINFO_METHOD_HANDLE)
            // AC: void recordRelocation(void*, void*, void*, uint16_t, uint16_t, int32_t)
            // AD: uint16_t getRelocTypeHind(void*)
            // AE: uint32_t getExpectedTargetArchitecture()
            // AF: uint32_t getJitFlags(CORJIT_FLAGS*, uint32_t)

            public const int TotalVtableCount = 0xB0;
        }

        protected unsafe override Delegate CreateCompileMethodDelegate(IntPtr compileMethod) {
            var del = new JitHookDelegateHolder(this, InvokeCompileMethodPtr, compileMethod).CompileMethodHook;
            return del;
        }

        private sealed class JitHookDelegateHolder {
            public readonly Core70Runtime Runtime;
            public readonly JitHookHelpersHolder JitHookHelpers;
            public readonly InvokeCompileMethodPtr InvokeCompileMethodPtr;
            public readonly IntPtr CompileMethodPtr;

            public readonly ThreadLocal<IAllocatedMemory> iCorJitInfoWrapper = new();
            public readonly ReadOnlyMemory<IAllocatedMemory> iCorJitInfoWrapperAllocs;
            public readonly IntPtr iCorJitInfoWrapperVtbl;

            public JitHookDelegateHolder(Core70Runtime runtime, InvokeCompileMethodPtr icmp, IntPtr compileMethod) {
                Runtime = runtime;
                JitHookHelpers = runtime.JitHookHelpers;
                InvokeCompileMethodPtr = icmp;
                CompileMethodPtr = compileMethod;

                iCorJitInfoWrapperVtbl = Marshal.AllocHGlobal(IntPtr.Size * ICorJitInfoWrapper.TotalVtableCount);
                iCorJitInfoWrapperAllocs = Runtime.arch.CreateNativeVtableProxyStubs(iCorJitInfoWrapperVtbl, ICorJitInfoWrapper.TotalVtableCount);
                MMDbgLog.Trace($"Allocated ICorJitInfo wrapper vtable at 0x{iCorJitInfoWrapperVtbl:x16}");

                // eagerly call ICMP to ensure that it's JITted before installing the hook
                unsafe { icmp.InvokeCompileMethod(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, default, 0, out _, out _); }
                // and the same with MarshalEx.(Get/Set)LastPInvokeError
                MarshalEx.SetLastPInvokeError(MarshalEx.GetLastPInvokeError());

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
                in V21.CORINFO_METHOD_INFO methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                out byte* nativeEntry,
                out uint nativeSizeOfCode) {

                nativeEntry = null;
                nativeSizeOfCode = 0;

                if (jit == IntPtr.Zero)
                    return CorJitResult.CORJIT_OK;

                var lastError = MarshalEx.GetLastPInvokeError();
                hookEntrancy++;
                try {

                    if (hookEntrancy == 1) {
                        try {
                            var corJitWrapper = iCorJitInfoWrapper.Value;
                            if (corJitWrapper is null) {
                                // we need to create corJitWrapper
                                var allocReq = new AllocationRequest(sizeof(ICorJitInfoWrapper)) {
                                    Alignment = IntPtr.Size,
                                    Executable = false
                                };
                                if (Runtime.System.MemoryAllocator.TryAllocate(allocReq, out var alloc)) {
                                    iCorJitInfoWrapper.Value = corJitWrapper = alloc;
                                }
                            }
                            // we still need to check if we were able to create it, because not creating it should not be a hard error
                            if (corJitWrapper is not null) {
                                var wrapper = (ICorJitInfoWrapper*) corJitWrapper.BaseAddress;
                                wrapper->Vtbl = iCorJitInfoWrapperVtbl;
                                wrapper->Wrapped = corJitInfo;
                                (*wrapper)[ICorJitInfoWrapper.HotCodeRW] = IntPtr.Zero;
                                (*wrapper)[ICorJitInfoWrapper.ColdCodeRW] = IntPtr.Zero;
                                corJitInfo = (IntPtr) wrapper;
                            }
                        } catch (Exception e) {
                            try {
                                MMDbgLog.Error($"Error while setting up the ICorJitInfo wrapper: {e}");
                            } catch {

                            }
                        }
                    }

                    var result = InvokeCompileMethodPtr.InvokeCompileMethod(CompileMethodPtr,
                        jit, corJitInfo, methodInfo, flags, out nativeEntry, out nativeSizeOfCode);

                    if (hookEntrancy == 1) {
                        try {
                            // we need to make sure that we set up the wrapper to continue
                            var corJitWrapper = iCorJitInfoWrapper.Value;
                            if (corJitWrapper is null)
                                return result;

                            ref var wrapper = ref *(ICorJitInfoWrapper*) corJitWrapper.BaseAddress;
                            var realEntry = wrapper[ICorJitInfoWrapper.HotCodeRW];

                            // This is the top level JIT entry point, do our custom stuff
                            RuntimeTypeHandle[]? genericClassArgs = null;
                            RuntimeTypeHandle[]? genericMethodArgs = null;

                            if (methodInfo.args.sigInst.classInst != null) {
                                genericClassArgs = new RuntimeTypeHandle[methodInfo.args.sigInst.classInstCount];
                                for (var i = 0; i < genericClassArgs.Length; i++) {
                                    genericClassArgs[i] = JitHookHelpers.GetTypeFromNativeHandle(methodInfo.args.sigInst.classInst[i]).TypeHandle;
                                }
                            }
                            if (methodInfo.args.sigInst.methInst != null) {
                                genericMethodArgs = new RuntimeTypeHandle[methodInfo.args.sigInst.methInstCount];
                                for (var i = 0; i < genericMethodArgs.Length; i++) {
                                    genericMethodArgs[i] = JitHookHelpers.GetTypeFromNativeHandle(methodInfo.args.sigInst.methInst[i]).TypeHandle;
                                }
                            }

                            RuntimeTypeHandle declaringType = JitHookHelpers.GetDeclaringTypeOfMethodHandle(methodInfo.ftn).TypeHandle;
                            RuntimeMethodHandle method = JitHookHelpers.CreateHandleForHandlePointer(methodInfo.ftn);

                            // TODO: pass down realEntry and given nativeEntry

                            Runtime.OnMethodCompiledCore(declaringType, method, genericClassArgs, genericMethodArgs, (IntPtr) nativeEntry, nativeSizeOfCode);
                        } catch {
                            // eat the exception so we don't accidentally bubble up to native code
                        }
                    }

                    return result;
                } finally {
                    hookEntrancy--;
                    MarshalEx.SetLastPInvokeError(lastError);
                }
            }
        }
    }
}
