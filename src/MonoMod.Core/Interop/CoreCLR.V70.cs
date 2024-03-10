using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{
    internal static unsafe partial class CoreCLR
    {

        public readonly struct InvokeAllocMemPtr
        {
            private readonly IntPtr methodPtr;
            public InvokeAllocMemPtr(
                delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    V70.AllocMemArgs*, // request
                    void
                > ptr
            )
            {
                methodPtr = (IntPtr)ptr;
            }

            public delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    V70.AllocMemArgs*, // request
                    void
                > InvokeAllocMem
                => (delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    V70.AllocMemArgs*, // request
                    void
                >)methodPtr;
        }

        [SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes",
            Justification = "It must be non-static to be able to inherit others, as it does. This allows the Core*Runtime types " +
            "to each reference exactly the version they represent, and the compiler automatically resolves the correct one without " +
            "needing duplicates.")]
        [SuppressMessage("Performance", "CA1852", Justification = "This type will be derived for .NET 8.")]
        public class V70 : V60
        {
            public static class ICorJitInfoVtable
            {

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
                // 96: InfoAccessType emptyStringLiteral(void**)
                // 97: uint32_t getFieldThreadLocalStoreID(CORINFO_FIELD_HANDLE, void**=null)
                // 98: void addActiveDependency(CORINFO_MODULE_HANDLE, CORINFO_MODULE_HANDLE)
                // 99: CORINFO_METHOD_HANDLE GetDelegateCtor(CORINFO_METHOD_HANDLE< CORINFO_CLASS_HANDLE, CORINFO_METHOD_HANDLE, DelegateCtorArgs*)
                // 9A: void MethodCompileComplete(CORINFO_METHOD_HANDLE)
                // 9B: bool getTailCallHelpers(CORINFO_RESOLVED_TOKEN*, CORINFO_SIG_INFO*, CORINFO_GET_TAILCALL_HELPERS_FLAGS, CORINFO_TAILCALL_HELPERS*)
                // 9C: bool convertPInvokeCalliToCall(CORINFO_RESOLVED_TOKEN*, bool)
                // 9D: bool notifyInstructionSetUsage(CORINFO_InstructionSet, bool)
                // 9E: void updateEntryPointForTailCall(CORINFO_CONST_LOOKUP*)

                // src/coreclr/inc/corjit.h
                // class ICorJitInfo : public ICorDynamicInfo
                // 9F: void allocMem(AllocMemArgs*)
                public const int AllocMemIndex = 0x9F;
                // A0: void reserveUnwindInfo(bool, bool, uint33_t)
                // A1: void allocUnwindInfo(uint8_t*, uint8_t*, uint32_t, uint32_t, uint32_t, uint8_t*, CorJitFuncKind)
                // A2: void* allocGCInfo(size_t)
                // A3: void setEHcount(unsigned)
                // A4: void setEHinfo(unsigned, CORINFO_EH_CLAUSE const*)
                // A5 bool logMsg(unsigned, char const*, va_list)
                // A6: int doAssert(char const*, int, char const*)
                // A7: void reportFatalError(CorJitResult)
                // A8: JITINTERFACE_HRESULT getPgoInstrumentationResults(CORINFO_METHOD_HANDLE, PgoInstrumentationSchema**, uint32_t*, uint8_t**, PgoSource*)
                // A9: JITINTERFACE_HRESULT allocPgoInstrumentationBySchema(CORINFO_METHOD_HANDLE, PgoInstrumentationSchema*)
                // AA: void recordCallSite(uint32_t, CORINFO_SIG_INFO*, CORINFO_METHOD_HANDLE)
                // AB: void recordRelocation(void*, void*, void*, uint16_t, uint16_t, int32_t)
                // AC: uint16_t getRelocTypeHind(void*)
                // AD: uint32_t getExpectedTargetArchitecture()
                // AE: uint32_t getJitFlags(CORJIT_FLAGS*, uint32_t)

                public const int TotalVtableCount = 0xAF;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct AllocMemArgs
            {
                // Input arguments
                public uint hotCodeSize;
                public uint coldCodeSize;
                public uint roDataSize;
                public uint xcptnsCount;
                public int flag; // CorJitAllocMemFlag

                // Output arguments
                public IntPtr hotCodeBlock;
                public IntPtr hotCodeBlockRW;
                public IntPtr coldCodeBlock;
                public IntPtr coldCodeBlockRW;
                public IntPtr roDataBlock;
                public IntPtr roDataBlockRW;
            };

            [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
            public delegate void AllocMemDelegate(
                IntPtr thisPtr, // ICorJitInfo*
                V70.AllocMemArgs* args
            );

            public static InvokeAllocMemPtr InvokeAllocMemPtr => new(&InvokeAllocMem);

            public static void InvokeAllocMem(
                IntPtr functionPtr,
                IntPtr thisPtr, // ICorJitInfo*
                V70.AllocMemArgs* args
            )
            {
                // this is present so that we can pre-JIT this method by calling it
                if (functionPtr == IntPtr.Zero)
                {
                    return;
                }

                var fnPtr =
                    (delegate* unmanaged[Thiscall]<
                        IntPtr, // ICorJitInfo* this
                        V70.AllocMemArgs*, // request
                        void
                    >)functionPtr;
                fnPtr(thisPtr, args);
            }

        }
    }
}
