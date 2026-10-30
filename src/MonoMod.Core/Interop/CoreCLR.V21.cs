using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{
    internal static unsafe partial class CoreCLR
    {
        public class V21
        {
            public static class ICorJitInfoVtable
            {
                // src\inc\corinfo.h:1977
                // class ICorStaticInfo
                // 00: DWORD getMethodAttribs(CORINFO_METHOD_HANDLE)
                // 01: void setMethodAttribs(CORINFO_METHOD_HANDLE, CorInfoMethodRuntimeFlags)
                // 02: void getMethodSig(CORINFO_METHOD_HANDLE, CORINFO_SIG_INFO *, CORINFO_CLASS_HANDLE)
                // 03: bool getMethodInfo(CORINFO_METHOD_HANDLE, CORINFO_METHOD_INFO *)
                // 04: CorInfoInline canInline(CORINFO_METHOD_HANDLE, CORINFO_METHOD_HANDLE, DWORD *)
                // 05: void reportInliningDecision(CORINFO_METHOD_HANDLE, CORINFO_METHOD_HANDLE, CorInfoInline, const char *)
                // 06: bool canTailCall(CORINFO_METHOD_HANDLE, CORINFO_METHOD_HANDLE, CORINFO_METHOD_HANDLE, bool)
                // 07: void reportTailCallDecision(CORINFO_METHOD_HANDLE, CORINFO_METHOD_HANDLE, bool, CorInfoTailCall, const char *)
                // 08: void getEHinfo(CORINFO_METHOD_HANDLE, unsigned int, CORINFO_EH_CLAUSE *)
                // 09: CORINFO_CLASS_HANDLE getMethodClass(CORINFO_METHOD_HANDLE)
                // 0A: CORINFO_MODULE_HANDLE getMethodModule(CORINFO_METHOD_HANDLE)
                // 0B: void getMethodVTableOffset(CORINFO_METHOD_HANDLE, unsigned int *, unsigned int *, bool *)
                // 0C: CORINFO_METHOD_HANDLE resolveVirtualMethod(CORINFO_METHOD_HANDLE, CORINFO_CLASS_HANDLE, CORINFO_CONTEXT_HANDLE)
                // 0D: CORINFO_METHOD_HANDLE getUnboxedEntry(CORINFO_METHOD_HANDLE, bool *)
                // 0E: CORINFO_CLASS_HANDLE getDefaultEqualityComparerClass(CORINFO_CLASS_HANDLE)
                // 0F: void expandRawHandleIntrinsic(CORINFO_RESOLVED_TOKEN *, CORINFO_GENERICHANDLE_RESULT *)
                // 10: CorInfoIntrinsics getIntrinsicID(CORINFO_METHOD_HANDLE, bool *)
                // 11: bool isInSIMDModule(CORINFO_CLASS_HANDLE)
                // 12: CorInfoUnmanagedCallConv getUnmanagedCallConv(CORINFO_METHOD_HANDLE)
                // 13: BOOL pInvokeMarshalingRequired(CORINFO_METHOD_HANDLE, CORINFO_SIG_INFO *)
                // 14: BOOL satisfiesMethodConstraints(CORINFO_CLASS_HANDLE, CORINFO_METHOD_HANDLE)
                // 15: BOOL isCompatibleDelegate(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE, CORINFO_METHOD_HANDLE, CORINFO_CLASS_HANDLE, BOOL *)
                // 16: CorInfoInstantiationVerification isInstantiationOfVerifiedGeneric(CORINFO_METHOD_HANDLE)
                // 17: void initConstraintsForVerification(CORINFO_METHOD_HANDLE, BOOL *, BOOL *)
                // 18: CorInfoCanSkipVerificationResult canSkipMethodVerification(CORINFO_METHOD_HANDLE)
                // 19: void methodMustBeLoadedBeforeCodeIsRun(CORINFO_METHOD_HANDLE)
                // 1A: CORINFO_METHOD_HANDLE mapMethodDeclToMethodImpl(CORINFO_METHOD_HANDLE)
                // 1B: void getGSCookie(GSCookie *, GSCookie **)
                // 1C: void resolveToken(CORINFO_RESOLVED_TOKEN *)
                // 1D: bool tryResolveToken(CORINFO_RESOLVED_TOKEN *)
                // 1E: void findSig(CORINFO_MODULE_HANDLE, unsigned int, CORINFO_CONTEXT_HANDLE, CORINFO_SIG_INFO *)
                // 1F: void findCallSiteSig(CORINFO_MODULE_HANDLE, unsigned int, CORINFO_CONTEXT_HANDLE, CORINFO_SIG_INFO *)
                // 20: CORINFO_CLASS_HANDLE getTokenTypeAsHandle(CORINFO_RESOLVED_TOKEN *)
                // 21: CorInfoCanSkipVerificationResult canSkipVerification(CORINFO_MODULE_HANDLE)
                // 22: BOOL isValidToken(CORINFO_MODULE_HANDLE, unsigned int)
                // 23: BOOL isValidStringRef(CORINFO_MODULE_HANDLE, unsigned int)
                // 24: BOOL shouldEnforceCallvirtRestriction(CORINFO_MODULE_HANDLE)
                // 25: CorInfoType asCorInfoType(CORINFO_CLASS_HANDLE)
                // 26: const char * getClassName(CORINFO_CLASS_HANDLE)
                // 27: const char * getClassNameFromMetadata(CORINFO_CLASS_HANDLE, const char **)
                // 28: CORINFO_CLASS_HANDLE getTypeInstantiationArgument(CORINFO_CLASS_HANDLE, unsigned int)
                // 29: int appendClassName(WCHAR **, int *, CORINFO_CLASS_HANDLE, BOOL, BOOL, BOOL)
                // 2A: BOOL isValueClass(CORINFO_CLASS_HANDLE)
                // 2B: BOOL canInlineTypeCheckWithObjectVTable(CORINFO_CLASS_HANDLE)
                // 2C: DWORD getClassAttribs(CORINFO_CLASS_HANDLE)
                // 2D: BOOL isStructRequiringStackAllocRetBuf(CORINFO_CLASS_HANDLE)
                // 2E: CORINFO_MODULE_HANDLE getClassModule(CORINFO_CLASS_HANDLE)
                // 2F: CORINFO_ASSEMBLY_HANDLE getModuleAssembly(CORINFO_MODULE_HANDLE)
                // 30: const char * getAssemblyName(CORINFO_ASSEMBLY_HANDLE)
                // 31: void * LongLifetimeMalloc(size_t)
                // 32: void LongLifetimeFree(void *)
                // 33: size_t getClassModuleIdForStatics(CORINFO_CLASS_HANDLE, CORINFO_MODULE_HANDLE *, void **)
                // 34: unsigned int getClassSize(CORINFO_CLASS_HANDLE)
                // 35: unsigned int getClassAlignmentRequirement(CORINFO_CLASS_HANDLE, BOOL)
                // 36: unsigned int getClassGClayout(CORINFO_CLASS_HANDLE, BYTE *)
                // 37: unsigned int getClassNumInstanceFields(CORINFO_CLASS_HANDLE)
                // 38: CORINFO_FIELD_HANDLE getFieldInClass(CORINFO_CLASS_HANDLE, int)
                // 39: BOOL checkMethodModifier(CORINFO_METHOD_HANDLE, int, BOOL)
                // 3A: CorInfoHelpFunc getNewHelper(CORINFO_RESOLVED_TOKEN *, CORINFO_METHOD_HANDLE)
                // 3B: CorInfoHelpFunc getNewArrHelper(CORINFO_CLASS_HANDLE)
                // 3C: CorInfoHelpFunc getCastingHelper(CORINFO_RESOLVED_TOKEN *, bool)
                // 3D: CorInfoHelpFunc getSharedCCtorHelper(CORINFO_CLASS_HANDLE)
                // 3E: CorInfoHelpFunc getSecurityPrologHelper(CORINFO_METHOD_HANDLE)
                // 3F: CORINFO_CLASS_HANDLE getTypeForBox(CORINFO_CLASS_HANDLE)
                // 40: CorInfoHelpFunc getBoxHelper(CORINFO_CLASS_HANDLE)
                // 41: CorInfoHelpFunc getUnBoxHelper(CORINFO_CLASS_HANDLE)
                // 42: bool getReadyToRunHelper(CORINFO_RESOLVED_TOKEN *, CORINFO_LOOKUP_KIND *, CorInfoHelpFunc, CORINFO_CONST_LOOKUP *)
                // 43: void getReadyToRunDelegateCtorHelper(CORINFO_RESOLVED_TOKEN *, CORINFO_CLASS_HANDLE, CORINFO_LOOKUP *)
                // 44: const char * getHelperName(CorInfoHelpFunc)
                // 45: CorInfoInitClassResult initClass(CORINFO_FIELD_HANDLE, CORINFO_METHOD_HANDLE, CORINFO_CONTEXT_HANDLE, BOOL)
                // 46: void classMustBeLoadedBeforeCodeIsRun(CORINFO_CLASS_HANDLE)
                // 47: CORINFO_CLASS_HANDLE getBuiltinClass(CorInfoClassId)
                // 48: CorInfoType getTypeForPrimitiveValueClass(CORINFO_CLASS_HANDLE)
                // 49: CorInfoType getTypeForPrimitiveNumericClass(CORINFO_CLASS_HANDLE)
                // 4A: BOOL canCast(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4B: BOOL areTypesEquivalent(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4C: TypeCompareState compareTypesForCast(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4D: TypeCompareState compareTypesForEquality(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4E: CORINFO_CLASS_HANDLE mergeClasses(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4F: CORINFO_CLASS_HANDLE getParentType(CORINFO_CLASS_HANDLE)
                // 50: CorInfoType getChildType(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE *)
                // 51: BOOL satisfiesClassConstraints(CORINFO_CLASS_HANDLE)
                // 52: BOOL isSDArray(CORINFO_CLASS_HANDLE)
                // 53: unsigned int getArrayRank(CORINFO_CLASS_HANDLE)
                // 54: void * getArrayInitializationData(CORINFO_FIELD_HANDLE, DWORD)
                // 55: CorInfoIsAccessAllowedResult canAccessClass(CORINFO_RESOLVED_TOKEN *, CORINFO_METHOD_HANDLE, CORINFO_HELPER_DESC *)
                // 56: const char * getFieldName(CORINFO_FIELD_HANDLE, const char **)
                // 57: CORINFO_CLASS_HANDLE getFieldClass(CORINFO_FIELD_HANDLE)
                // 58: CorInfoType getFieldType(CORINFO_FIELD_HANDLE, CORINFO_CLASS_HANDLE *, CORINFO_CLASS_HANDLE)
                // 59: unsigned int getFieldOffset(CORINFO_FIELD_HANDLE)
                // 5A: bool isWriteBarrierHelperRequired(CORINFO_FIELD_HANDLE)
                // 5B: void getFieldInfo(CORINFO_RESOLVED_TOKEN *, CORINFO_METHOD_HANDLE, CORINFO_ACCESS_FLAGS, CORINFO_FIELD_INFO *)
                // 5C: bool isFieldStatic(CORINFO_FIELD_HANDLE)
                // 5D: void getBoundaries(CORINFO_METHOD_HANDLE, unsigned int *, DWORD **, ICorDebugInfo::BoundaryTypes *)
                // 5E: void setBoundaries(CORINFO_METHOD_HANDLE, ULONG32, ICorDebugInfo::OffsetMapping *)
                // 5F: void getVars(CORINFO_METHOD_HANDLE, ULONG32 *, ICorDebugInfo::ILVarInfo **, bool *)
                // 60: void setVars(CORINFO_METHOD_HANDLE, ULONG32, ICorDebugInfo::NativeVarInfo *)
                // 61: void * allocateArray(ULONG)
                // 62: void freeArray(void *)
                // 63: CORINFO_ARG_LIST_HANDLE getArgNext(CORINFO_ARG_LIST_HANDLE)
                // 64: CorInfoTypeWithMod getArgType(CORINFO_SIG_INFO *, CORINFO_ARG_LIST_HANDLE, CORINFO_CLASS_HANDLE *)
                // 65: CORINFO_CLASS_HANDLE getArgClass(CORINFO_SIG_INFO *, CORINFO_ARG_LIST_HANDLE)
                // 66: CorInfoType getHFAType(CORINFO_CLASS_HANDLE)
                // 67: ULONG GetErrorMessage(int, ULONG)
                // 68: int FilterException(struct _EXCEPTION_POINTERS *)
                // 69: void HandleException(struct _EXCEPTION_POINTERS *)
                // 6A: void ThrowExceptionForJitResult(int)
                // 6B: void ThrowExceptionForHelper(const CORINFO_HELPER_DESC *)
                // 6C: bool runWithErrorTrap(void (*)(void *), void *)
                // 6D: void getEEInfo(CORINFO_EE_INFO *)
                // 6E: mdMethodDef getMethodDefFromMethod(CORINFO_METHOD_HANDLE)
                // 6F: const char * getMethodName(CORINFO_METHOD_HANDLE, const char **)
                // 70: const char * getMethodNameFromMetadata(CORINFO_METHOD_HANDLE, const char **, const char **)
                // 71: unsigned int getMethodHash(CORINFO_METHOD_HANDLE)
                // 72: size_t findNameOfToken(CORINFO_MODULE_HANDLE, mdToken, char *, size_t)
                // 73: bool getSystemVAmd64PassStructInRegisterDescriptor(CORINFO_CLASS_HANDLE, SYSTEMV_AMD64_CORINFO_STRUCT_REG_PASSING_DESCRIPTOR *)
                //
                // src\inc\corinfo.h:2892
                // class ICorDynamicInfo
                // 74: DWORD getThreadTLSIndex(void **)
                // 75: const void * getInlinedCallFrameVptr(void **)
                // 76: void * getHelperFtn(CorInfoHelpFunc, void **)
                // 77: void getFunctionEntryPoint(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP *, CORINFO_ACCESS_FLAGS)
                // 78: void getFunctionFixedEntryPoint(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP *)
                // 79: void * getMethodSync(CORINFO_METHOD_HANDLE, void **)
                // 7A: CorInfoHelpFunc getLazyStringLiteralHelper(CORINFO_MODULE_HANDLE)
                // 7B: CORINFO_MODULE_HANDLE embedModuleHandle(CORINFO_MODULE_HANDLE, void **)
                // 7C: CORINFO_CLASS_HANDLE embedClassHandle(CORINFO_CLASS_HANDLE, void **)
                // 7D: CORINFO_METHOD_HANDLE embedMethodHandle(CORINFO_METHOD_HANDLE, void **)
                // 7E: CORINFO_FIELD_HANDLE embedFieldHandle(CORINFO_FIELD_HANDLE, void **)
                // 7F: void embedGenericHandle(CORINFO_RESOLVED_TOKEN *, BOOL, CORINFO_GENERICHANDLE_RESULT *)
                // 80: CORINFO_LOOKUP_KIND getLocationOfThisType(CORINFO_METHOD_HANDLE)
                // 81: void * getPInvokeUnmanagedTarget(CORINFO_METHOD_HANDLE, void **)
                // 82: void * getAddressOfPInvokeFixup(CORINFO_METHOD_HANDLE, void **)
                // 83: void getAddressOfPInvokeTarget(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP *)
                // 84: LPVOID GetCookieForPInvokeCalliSig(CORINFO_SIG_INFO *, void **)
                // 85: bool canGetCookieForPInvokeCalliSig(CORINFO_SIG_INFO *)
                // 86: CORINFO_JUST_MY_CODE_HANDLE getJustMyCodeHandle(CORINFO_METHOD_HANDLE, CORINFO_JUST_MY_CODE_HANDLE **)
                // 87: void GetProfilingHandle(BOOL *, void **, BOOL *)
                // 88: void getCallInfo(CORINFO_RESOLVED_TOKEN *, CORINFO_RESOLVED_TOKEN *, CORINFO_METHOD_HANDLE, CORINFO_CALLINFO_FLAGS, CORINFO_CALL_INFO *)
                // 89: BOOL canAccessFamily(CORINFO_METHOD_HANDLE, CORINFO_CLASS_HANDLE)
                // 8A: BOOL isRIDClassDomainID(CORINFO_CLASS_HANDLE)
                // 8B: unsigned int getClassDomainID(CORINFO_CLASS_HANDLE, void **)
                // 8C: void * getFieldAddress(CORINFO_FIELD_HANDLE, void **)
                // 8D: CORINFO_VARARGS_HANDLE getVarArgsHandle(CORINFO_SIG_INFO *, void **)
                // 8E: bool canGetVarArgsHandle(CORINFO_SIG_INFO *)
                // 8F: InfoAccessType constructStringLiteral(CORINFO_MODULE_HANDLE, mdToken, void **)
                // 90: InfoAccessType emptyStringLiteral(void **)
                // 91: DWORD getFieldThreadLocalStoreID(CORINFO_FIELD_HANDLE, void **)
                // 92: void setOverride(ICorDynamicInfo *, CORINFO_METHOD_HANDLE)
                // 93: void addActiveDependency(CORINFO_MODULE_HANDLE, CORINFO_MODULE_HANDLE)
                // 94: CORINFO_METHOD_HANDLE GetDelegateCtor(CORINFO_METHOD_HANDLE, CORINFO_CLASS_HANDLE, CORINFO_METHOD_HANDLE, DelegateCtorArgs *)
                // 95: void MethodCompileComplete(CORINFO_METHOD_HANDLE)
                // 96: void * getTailCallCopyArgsThunk(CORINFO_SIG_INFO *, CorInfoHelperTailCallSpecialHandling)
                //
                // src\inc\corjit.h:318
                // class ICorJitInfo
                // 97: IEEMemoryManager * getMemoryManager()
                // 98: void allocMem(ULONG, ULONG, ULONG, ULONG, CorJitAllocMemFlag, void **, void **, void **)
                public const int AllocMemIndex = 0x98;
                // 99: void reserveUnwindInfo(BOOL, BOOL, ULONG)
                // 9A: void allocUnwindInfo(BYTE *, BYTE *, ULONG, ULONG, ULONG, BYTE *, CorJitFuncKind)
                // 9B: void * allocGCInfo(size_t)
                // 9C: void yieldExecution()
                // 9D: void setEHcount(unsigned int)
                // 9E: void setEHinfo(unsigned int, const CORINFO_EH_CLAUSE *)
                // 9F: BOOL logMsg(unsigned int, const char *, va_list)
                // A0: int doAssert(const char *, int, const char *)
                // A1: void reportFatalError(CorJitResult)
                // A2: void recordCallSite(ULONG, CORINFO_SIG_INFO *, CORINFO_METHOD_HANDLE)
                // A3: void recordRelocation(void *, void *, WORD, WORD, int)
                // A4: WORD getRelocTypeHint(void *)
                // A5: void getModuleNativeEntryPointRange(void **, void **)
                // A6: DWORD getExpectedTargetArchitecture()
                // A7: DWORD getJitFlags(CORJIT_FLAGS *, DWORD)
                public const int GetJitFlagsIndex = 0xA7;

                public const int TotalVtableCount = 0xA8;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct CORINFO_SIG_INST
            {
                public uint classInstCount;
                public IntPtr* classInst; // CORINFO_CLASS_HANDLE* // (representative, not exact) instantiation for class type variables in signature
                public uint methInstCount;
                public IntPtr* methInst; // CORINFO_CLASS_HANDLE* // (representative, not exact) instantiation for method type variables in signature
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct CORINFO_SIG_INFO
            {
                public int callConv; // CorInfoCallConv
                public IntPtr retTypeClass; // CORINFO_CLASS_HANDLE // if the return type is a value class, this is its handle (enums are normalized)
                public IntPtr retTypeSigClass; // CORINFO_CLASS_HANDLE // returns the value class as it is in the sig (enums are not converted to primitives)
                public byte retType; // CorInfoType : 8
                public byte flags; // unsigned : 8 // used by IL stubs code
                public ushort numArgs; // unsigned : 16 
                public CORINFO_SIG_INST sigInst; // information about how type variables are being instantiated in generic code
                public IntPtr args; // CORINFO_ARG_LIST_HANDLE
                public IntPtr pSig; // COR_SIGNATURE*
                public uint sbSig;
                public IntPtr scope; // CORINFO_MODULE_HANDLE // passed to getArgClass
                public uint token; // mdToken (aka ULONG32 aka unsigned int)
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct CORINFO_METHOD_INFO
            {
                public IntPtr ftn;   // CORINFO_METHOD_HANDLE
                public IntPtr scope; // CORINFO_MODULE_HANDLE
                public byte* ILCode;
                public uint ILCodeSize;
                public uint maxStack;
                public uint EHcount;
                public int options; // CorInfoOptions
                public int regionKind; // CorInfoRegionKind
                public CORINFO_SIG_INFO args;
                public CORINFO_SIG_INFO locals;
            }

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate CorJitResult CompileMethodDelegate(
                IntPtr thisPtr, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** nativeEntry,
                uint* nativeSizeOfCode
            );

            public static InvokeCompileMethodPtr InvokeCompileMethodPtr => new(&InvokeCompileMethod);

            // TODO: abstract away CORINFO_METHOD_INFO
            public static CorJitResult InvokeCompileMethod(
                IntPtr functionPtr,
                IntPtr thisPtr, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** pNativeEntry,
                uint* pNativeSizeOfCode
            )
            {
                // this is present so that we can pre-JIT this method by calling it
                if (functionPtr == IntPtr.Zero)
                {
                    *pNativeEntry = null;
                    *pNativeSizeOfCode = 0;
                    return CorJitResult.CORJIT_OK;
                }

                var fnPtr =
                    (delegate* unmanaged[Stdcall]<
                        IntPtr, IntPtr, CORINFO_METHOD_INFO*,
                        uint, byte**, uint*,
                        CorJitResult
                    >)functionPtr;

                return fnPtr(thisPtr, corJitInfo, methodInfo, flags, pNativeEntry, pNativeSizeOfCode);
            }

            public static InvokeGetJitFlagsPtr InvokeGetJitFlagsPtr => new(&InvokeGetJitFlags);

            public static uint InvokeGetJitFlags(
                IntPtr functionPtr,
                IntPtr thisPtr,
                void* flags,
                uint sizeInBytes
            )
            {
                var fnPtr =
                    (delegate* unmanaged[Thiscall]<
                        IntPtr,
                        void*,
                        uint,
                        uint
                    >)functionPtr;
                return fnPtr(thisPtr, flags, sizeInBytes);
            }
        }
    }
}
