using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

#pragma warning disable CA1069 // Enums values should not be duplicated
// Any time we do so is to replicate the name of the flags in the runtime itself.

namespace MonoMod.Core.Interop
{
    internal static unsafe partial class CoreCLR
    {
        public readonly struct InvokeCompileMethodHookPostPtr
        {
            private readonly IntPtr methodPtr;
            public InvokeCompileMethodHookPostPtr(
                delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitCompiler*
                    IntPtr, // ICorJitInfo*
                    V21.CORINFO_METHOD_INFO*, // CORINFO_METHOD_INFO*
                    uint,
                    byte**,
                    uint*,
                    CorJitResult,
                    V60.AllocMemArgs*,
                    CorJitResult
                > ptr
            )
            {
                methodPtr = (IntPtr)ptr;
            }

            public delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitCompiler*
                    IntPtr, // ICorJitInfo*
                    V21.CORINFO_METHOD_INFO*, // CORINFO_METHOD_INFO*
                    uint,
                    byte**,
                    uint*,
                    CorJitResult,
                    V60.AllocMemArgs*,
                    CorJitResult
                > InvokeCompileMethodHookPost
                => (delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitCompiler*
                    IntPtr, // ICorJitInfo*
                    V21.CORINFO_METHOD_INFO*, // CORINFO_METHOD_INFO*
                    uint,
                    byte**,
                    uint*,
                    CorJitResult,
                    V60.AllocMemArgs*,
                    CorJitResult
                >)methodPtr;
        }

        public readonly struct InvokeAllocMemPtr
        {
            private readonly IntPtr methodPtr;
            public InvokeAllocMemPtr(
                delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    V60.AllocMemArgs*, // request
                    void
                > ptr
            )
            {
                methodPtr = (IntPtr)ptr;
            }

            public delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    V60.AllocMemArgs*, // request
                    void
                > InvokeAllocMem
                => (delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    V60.AllocMemArgs*, // request
                    void
                >)methodPtr;
        }

        [SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes",
            Justification = "It must be non-static to be able to inherit others, as it does. This allows the Core*Runtime types " +
            "to each reference exactly the version they represent, and the compiler automatically resolves the correct one without " +
            "needing duplicates.")]
        public partial class V60 : V50
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
                //  6: void reportInliningDecision(MethodDesc*, MethodDesc*, CorInfoInline, char const*)
                //  7: bool canTailCall(MethodDesc*, MethodDesc*, MethodDesc*, bool)
                //  8: void reportTailCallDecision(MethodDesc*, MethodDesc*, bool, CorInfoTailCall, char const*)
                //  9: void getEHInfo(MethodDesc*, unsigned, CORINFO_EH_CLAUSE*)
                //  A: CORINFO_CLASS_HANDLE getMethodClass(MethodDesc*)
                //  B: CORINFO_MODULE_HANDLE getMethodModule(MethodDesc*)
                //  C: void getMethodVTableOffset(MethodDesc*, unsigned*, unsigned*, bool*)
                //  D: bool resolveVirtualMethod(CORINFO_DEVIRTUALIZATION_INFO*)
                //  E: MethodDesc* getUnboxedEntry(MethodDesc*, bool*)
                //  F: CORINFO_CLASS_HANDLE getDefaultComparerClass(CORINFO_CLASS_HANDLE)
                // 10: CORINFO_CLASS_HANDLE getDefaultEqualityComparerClass(CORINFO_CLASS_HANDLE)
                // 11: void expandRawHandleIntrinisc(CORINFO_RESOLVED_TOKEN*, CORINFO_GENERICHANDLE_RESULT*)
                // 12: CorInfoIntrinsics getIntrinsicID(CORINFO_METHOD_HANDLE*, bool*)
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
                // 2D: bool isStructRequiringStackAllocRetBuf(CORINFO_CLASS_HANDLE)
                // 2E: CORINFO_MODULE_HANDLE getClassModule(CORINFO_CLASS_HANDLE)
                // 2F: CORINFO_ASSEMBLY_HANDLE getModuleAssembly(CORINFO_MODULE_HANDLE)
                // 30: char const* getAssemblyName()CORINFO_ASSEMBLY_HANDLE)
                // 31: void* LongLifetimeMalloc(size_t)
                // 32: void LogLifetimemFree(void*)
                // 33: size_t getClassModuleIdForStatics(CORINFO_CLASS_HANDLE, CORINFO_MODULE_HANDLE*, void**)
                // 34: unsigned getClassSize(CORINFO_CLASS_HANDLE)
                // 35: unsigned getHeapClassSize(CORINFO_CLASS_HANDLE)
                // 36: bool canAllocateOnStaci(CORINFO_CLASS_HANDLE)
                // 37: unsigned getClassAlignmentRequirement(CORINFO_CLASS_HANDLE, bool=false)
                // 38: unsigned getClassGClayout(CORINFO_CLASS_HANDLE, uint8_t*)
                // 39: unsigned getClassNumInstanceFields(CORINFO_CLASS_HANDLE)
                // 3A: CORINFO_FIELD_HANDLE getFieldInClass(CORINFO_CLASS_HANDLE, int32_t)
                // 3B: bool checkMethodModifier(MethodTable*, char const*, bool)
                // 3C: CorInfoHelpFunc getNewHelper(CORINFO_RESOLVED_TOKEN*, MethodTable*, bool*)
                // 3D: CorInfoHelpFunc getNewArrHelper(CORINFO_CLASS_HANDLE)
                // 3E: CorInfoHelpFunc getCastingHelper(CORINFO_RESOLVED_TOKEN*, bool)
                // 3F: CorInfoHelpFunc getCharedCCtorHelper(CORINFO_CLASS_HANDLE)
                // 40: CORINFO_CLASS_HANDLE getTypeForBox(CORINFO_CLASS_HANDLE)
                // 41: CorInfoHelpFunc getBoxHelper(CORINFO_CLASS_HANDLE)
                // 42: CorInfoHelpFunc getUnBoxHelper(CORINFO_CLASS_HANDLE)
                // 43: bool getReadyToRunHelper(CORINFO_RESOLVED_TOKEN*, CORINFO_LOOKUP_KIND*, CorInfoHelpFunc, CORINFO_CONST_LOOKUP*)
                // 44: void getReadyToRunDelegateCtorHelper(CORINFO_RESOLVED_TOKEN*, mdToken, CORINFO_CLASS_HANDLE, CORINFO_LOOKUP*)
                // 45: char const* getHelperName(CorInfoHelpFunc)
                // 46: CorInfoInitClassResult initClass(CORINFO_FIELD_HANDLE, CORINFO_METHOD_HANDLE, CORINFO_CONTEXT_HANDLE)
                // 47: void classMustBeLoadedBeforeCodeIsRun(CORINFO_CLASS_HANDLE)
                // 48: CORINFO_CLASS_HANDLE getBuiltinClass(CorInfoClassId)
                // 49: CorInfoType getTypeForPrimitiveValueClass(CORINFO_CLASS_HANDLE)
                // 4A: CorInfoType getTypeForPrimitiveNumericClass(CORINFO_CLASS_HANDLE)
                // 4B: bool canCast(CORINFO_CLASS_HANDLE)
                // 4C: bool areTypesEquivalent(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4D: TypeCompareState compareTypesForCast(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 4E: TypeCompareState compareTypesForEquality(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE
                // 4F: CORINFO_CLASS_HANDLE mergeClasses(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 50: bool isMoreSpecificType(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE)
                // 51: CORINFO_CLASS_HANDLE getParentType(CORINFO_CLASS_HANDLE)
                // 52: CorInfoType getChildType(CORINFO_CLASS_HANDLE, CORINFO_CLASS_HANDLE*)
                // 53: bool satisfiesClassConstraints(CORINFO_CLASS_HANDLE)
                // 54: bool isSDArray(CORINFO_CLASS_HANDLE)
                // 55: unsigned getArrayRank(CORINFO_CLASS_HANDLE)
                // 56: void* getArrayInitializationData(CORINFO_FIELD_HANDLE, uint32_t)
                // 57: CorInfoIsAccessAllowedResult canAccessClass(CORINFO_RESOLVED_TOKEN*, CORINFO_METHOD_HANDLE, CORINFO_HELPER_DESC*)
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
                // 62: void* allocateArray(size_t)
                // 63: void freeArray(void*)
                // 64: CORINFO_ARG_LIST_HANDLE getArgNext(CORINFO_ARG_LIST_HANDLE)
                // 65: CorInfoTypeWithMod getArgType(CORINFO_SIG_INFO*, CORINFO_ARG_LIST_HANDLE, CORINFO_CLASS_HANDLE*)
                // 66: CORINFO_CLASS_HANDLE getArgClass(CORINFO_SIG_INFO*, CORINFO_ARG_LIST_HANDLE)
                // 67: CorInfoHFAElemType getHFAType(CORINFO_CLASS_HANDLE)
                // 68: JITINTERFACE_HRESULT GetErrorHRESULT(struct _EXCEPTION_POINTERS*)
                // 69: uint32_t GetErrorMessage(char16_t*, uint32_t)
                // 6A: int FilterException(struct _EXCEPTION_POINTERS*)
                // 6B: void ThrowExceptionForJitResult(JITINTERFACE_HRESULT)
                // 6C: void ThrowExceptionForHelper(CORINFO_HELPER_DESC const*)
                // 6D: bool runWithErrorTrap(errorTrapFunction, void*)
                // 6E: bool runWithSPMIErrorTrap(errorTrapFunction, void*)
                // 6F: void getEEInfo(CORINFO_EE_INFO*)
                // 70: char16_t const* getJitTimeLogFilename()
                // 71: mdMethodDef getMethodDefFromMethod(CORINFO_METHOD_HANDLE)
                // 72: char const* getMethodName(CORINFO_METHOD_HANDLE, char const**)
                // 73: char const* getMethodNameFromMetadata(CORINFO_METHOD_HANDLE, char const**, char const**, char const**)
                // 74: unsigned getMethodHash(CORINFO_METHOD_HANDLE)
                // 75: size_t findNameOFToken(CORINFO_MODULE_HANDLE, mdToken, char*, size_t)
                // 76: bool getSystemVAmd64PassStructInRegisterDescriptor(CORINFO_CLASS_HANDLE, ...*)

                // src/coreclr/inc/corinfo.h
                // class ICorDynamicInfo : public ICorStaticInfo
                // 77: uint32_t getThreadTLSIndex(void**=null)
                // 78: void const* getInlinedCallFrameVptr(void**=null)
                // 79: int32_t* getAddrOfCaptureThreadGlobal(void**=null)
                // 7A: void* getHelperFtn(CorInfoHelpFunc,void**=null)
                // 7B: void getFunctionEntryPoint(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP*, CORINFO_ACCESS_FLAGS=ANY)
                // 7C: void getFunctionFixedEntryPoint(CORINFO_METHOD_HANDLE, bool, CORINFO_CONST_LOOKUP*)
                // 7D: void* getMethodSync(CORINFO_METHOD_HANDLE, void**=null)
                // 7E: CorInfoHelpFunc getLazyStringLiteralHelper(CORINFO_MODULE_HANDLE)
                // 7F: CORINFO_MODULE_HANDLE embedModuleHandle(CORINFO_MODULE_HANDLE, void**=null)
                // 80: CORINFO_CLASS_HANDLE embedClassHandle(CORINFO_CLASS_HANDLE, void**=null)
                // 81: CORINFO_METHOD_HANDLE embedMethodHandle(CORINFO_METHOD_HANDLE, void**=null)
                // 82: CORINFO_FIELD_HANDLE embedFieldHandle(CORINFO_FIELD_HANDLE, void**=null)
                // 83: void embedGenericHandle(CORINFO_RESOLVED_TOKEN*, bool, CORINFO_GENERICHANDLE_RESULT*)
                // 84: void getLocationOfThisType(CORINFO_METHOD_HANDLE, CORINFO_LOOKUP_KIND*)
                // 85: void getAddressOfPInvokeTarget(CORINFO_METHOD_HANDLE, CORINFO_CONST_LOOKUP*)
                // 86: void* GetCookieForPINvokeCalliSig(CORINFO_SIG_INFO*, void**=null)
                // 87: bool canGetCookieForPInvokeCalliSig(CORINFO_SIG_INFO*)
                // 88: CORINFO_JUST_MY_CODE_HANDLE getJustMyCodeHandle(CORINFO_METHOD_HANDLE, CORINFO_JUST_MY_CODE_HANDLE**=null)
                // 89: void GetProfilingHandle(bool*, void**, bool*)
                // 8A: void getCallInfo(CORINFO_RESOLVED_TOKEN*, CORINFO_RESOLVED_TOKEN*, CORINFO_METHOD_HANDLE, CORINFO_CALLINFO_FLAGS, CORINFO_CALL_INFO*)
                // 8B: bool canAccessFamily(CORINFO_METHOD_HANDLE, CORINFO_CLASS_HANDLE)
                // 8C: bool isRIDClassDomainID(CORINFO_CLASS_HANDLE)
                // 8D: unsigned getClassDomainID(CORINFO_CLASS_HANDLE, void**=null)
                // 8E: void* getFieldAddress(CORINFO_FIELD_HANDLE, void**=null)
                // 8F: CORINFO_CLASS_HANDLE getStaticFieldCurrentClass(CORINFO_FIELD_HANDLE, bool*=null)
                // 90: CORINFO_VARARGS_HANDLE getVarArgsHandle(CORINFO_SIG_INFO*, void**=null)
                // 91: bool canGetVarArgsHandle(CORINFO_SIG_INFO*)
                // 92: InfoAccessType constructStringLiteral(CORINFO_MODULE_HANDLE, mdToken, void**)
                // 93: InfoAccessType emptyStringLiteral(void**)
                // 94: uint32_t getFieldThreadLocalStoreID(CORINFO_FIELD_HANDLE, void**=null)
                // 95: void setOverride(ICorDynamicInfo*, CORINFO_METHOD_HANDLE)
                // 96: void addActiveDependency(CORINFO_MODULE_HANDLE, CORINFO_MODULE_HANDLE)
                // 97: CORINFO_METHOD_HANDLE GetDelegateCtor(CORINFO_METHOD_HANDLE< CORINFO_CLASS_HANDLE, CORINFO_METHOD_HANDLE, DelegateCtorArgs*)
                // 98: void MethodCompileComplete(CORINFO_METHOD_HANDLE)
                // 99: bool getTailCallHelpers(CORINFO_RESOLVED_TOKEN*, CORINFO_SIG_INFO*, CORINFO_GET_TAILCALL_HELPERS_FLAGS, CORINFO_TAILCALL_HELPERS*)
                // 9A: bool convertPInvokeCalliToCall(CORINFO_RESOLVED_TOKEN*, bool)
                // 9B: bool notifyInstructionSetUsage(CORINFO_InstructionSet, bool)

                // src/coreclr/inc/corjit.h
                // class ICorJitInfo : public ICorDynamicInfo
                // 9C: void allocMem(AllocMemArgs*)
                public const int AllocMemIndex = 0x9C;
                // 9D: void reserveUnwindInfo(bool, bool, uint33_t)
                // 9E: void allocUnwindInfo(uint8_t*, uint8_t*, uint32_t, uint32_t, uint32_t, uint8_t*, CorJitFuncKind)
                // 9F: void* allocGCInfo(size_t)
                // A0: void setEHcount(unsigned)
                // A1: void setEHinfo(unsigned, CORINFO_EH_CLAUSE const*)
                // A2 bool logMsg(unsigned, char const*, va_list)
                // A3: int doAssert(char const*, int, char const*)
                // A4: void reportFatalError(CorJitResult)
                // A5: JITINTERFACE_HRESULT getPgoInstrumentationResults(CORINFO_METHOD_HANDLE, PgoInstrumentationSchema**, uint32_t*, uint8_t**, PgoSource*)
                // A6: JITINTERFACE_HRESULT allocPgoInstrumentationBySchema(CORINFO_METHOD_HANDLE, PgoInstrumentationSchema*)
                // A7: void recordCallSite(uint32_t, CORINFO_SIG_INFO*, CORINFO_METHOD_HANDLE)
                // A8: void recordRelocation(void*, void*, void*, uint16_t, uint16_t, int32_t)
                // A9: uint16_t getRelocTypeHint(void*)
                // AA: uint32_t getExpectedTargetArchitecture()
                // AB: uint32_t getJitFlags(CORJIT_FLAGS*, uint32_t)
                // AC: bool doesFieldBelongToClass(CORINFO_FIELD_HANDLE, CORINFO_CLASS_HANDLE)
                public const int TotalVtableCount = 0xAD;
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
                V60.AllocMemArgs* args
            );

            public static InvokeAllocMemPtr InvokeAllocMemPtr => new(&InvokeAllocMem);

            public static void InvokeAllocMem(
                IntPtr functionPtr,
                IntPtr thisPtr, // ICorJitInfo*
                V60.AllocMemArgs* args
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
                        V60.AllocMemArgs*, // request
                        void
                    >)functionPtr;
                fnPtr(thisPtr, args);
            }

            [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
            public new delegate CorJitResult CompileMethodDelegate(
                IntPtr thisPtr, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** nativeEntry,
                uint* nativeSizeOfCode
            );

            public new static InvokeCompileMethodPtr InvokeCompileMethodPtr => new(&InvokeCompileMethod);

            public new static CorJitResult InvokeCompileMethod(
                IntPtr functionPtr,
                IntPtr thisPtr, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** nativeEntry,
                uint* nativeSizeOfCode
            )
            {
                // this is present so that we can pre-JIT this method by calling it
                if (functionPtr == IntPtr.Zero)
                {
                    *nativeEntry = null;
                    *nativeSizeOfCode = 0;
                    return CorJitResult.CORJIT_OK;
                }

                var fnPtr =
                    (delegate* unmanaged[Thiscall]<
                        IntPtr, IntPtr, CORINFO_METHOD_INFO*,
                        uint, byte**, uint*,
                        CorJitResult
                    >)functionPtr;
                return fnPtr(thisPtr, corJitInfo, methodInfo, flags, nativeEntry, nativeSizeOfCode);
            }

            [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
            public delegate CorJitResult CompileMethodHookPostDelegate(
                IntPtr thisPtr, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** nativeEntry,
                uint* nativeSizeOfCode,
                CorJitResult res,
                AllocMemArgs* allocMemArgs
            );

            public static InvokeCompileMethodHookPostPtr InvokeCompileMethodHookPostPtr => new(&InvokeCompileMethodHookPost);

            public static CorJitResult InvokeCompileMethodHookPost(
                IntPtr functionPtr,
                IntPtr thisPtr, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** nativeEntry,
                uint* nativeSizeOfCode,
                CorJitResult res,
                AllocMemArgs *pArgs)
            {
                // this is present so that we can pre-JIT this method by calling it
                if (functionPtr == IntPtr.Zero)
                    return res;

                var fnPtr =
                    (delegate* unmanaged[Thiscall]<
                        IntPtr, IntPtr, CORINFO_METHOD_INFO*,
                        uint, byte**, uint*,
                        CorJitResult, AllocMemArgs *, CorJitResult
                    >)functionPtr;
                return fnPtr(thisPtr, corJitInfo, methodInfo, flags, nativeEntry, nativeSizeOfCode, res, pArgs);
            }

            public enum MethodClassification
            {
                IL = 0,
                FCall = 1,
                NDirect = 2,
                EEImpl = 3,
                Array = 4,
                Instantiated = 5,
                ComInterop = 6,
                Dynamic = 7,
            }

            [Flags]
            public enum MethodDescClassification : ushort
            {
                ClassificationMask = 0x0007,
                HasNonVtableSlot = 0x0008,
                MethodImpl = 0x0010,
                HasNativeCodeSlot = 0x0020,
                HasComPlusCallInfo = 0x0040,
                Static = 0x0080,
                Duplicate = 0x0400,
                VerifiedState = 0x0800,
                Verifiable = 0x1000,
                NotInline = 0x2000,
                Synchronized = 0x4000,
                RequiresFullSlotNumber = 0x8000,
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct RelativePointer
            {
                private nint m_delta;
                public RelativePointer(nint delta)
                {
                    m_delta = delta;
                }
                // in the runtime, there's a bunch of song-and-dance to pass in the address because of DAccess.
                // We can ignore all that, because we are in-process.
                public void* Value
                {
                    get
                    {
                        var delta = m_delta;
                        return delta == 0
                            ? null
                            : Unsafe.AsPointer(ref Unsafe.AddByteOffset(ref this, delta));
                    }
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct RelativeFixupPointer
            {
                private nint m_delta;

                public const nint FIXUP_POINTER_INDIRECTION = 1;
                public void* Value
                {
                    get
                    {
                        var delta = m_delta;
                        if (delta == 0)
                            return null;

                        var addr = (nint)Unsafe.AsPointer(ref Unsafe.AddByteOffset(ref this, delta));
                        if ((addr & FIXUP_POINTER_INDIRECTION) != 0)
                        {
                            addr = *(nint*)(addr - FIXUP_POINTER_INDIRECTION);
                        }
                        return (void*)addr;
                    }
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MethodDesc
            {
                public static readonly nuint Alignment = IntPtr.Size == 8 ? ((nuint)1 << 3) : ((nuint)1 << 2);

                [Flags]
                public enum Flags3 : ushort
                {
                    TokenRemainderMask = 0x3FFF,
                    HasForwardedValuetypeParameter = 0x4000,
                    ValueTypeParametersWalked = 0x4000,
                    DoesNotHaveEquivalentValuetypeParameters = 0x8000,
                }

                public Flags3 m_wFlags3AndTokenRemainder;
                public byte m_chunkIndex;

                [Flags]
                public enum Flags2 : byte
                {
                    HasStableEntryPoint = 0x01,
                    HasPrecode = 0x02,
                    IsUnboxingStub = 0x04,
                    IsJitIntrinsic = 0x10,
                    IsEligibleForTieredCompilation = 0x20,
                    RequiresCovariantReturnTypeChecking = 0x40,
                }
                public Flags2 m_bFlags2;

                public const ushort PackedSlot_SlotMask = 0x03FF;
                public const ushort PackedSlot_NameHashMask = 0xFC00;
                public ushort m_wSlotNumber;

                public MethodDescClassification m_wFlags;

                public ushort SlotNumber => m_wFlags.Has(MethodDescClassification.RequiresFullSlotNumber) ? m_wSlotNumber : (ushort)(m_wSlotNumber & PackedSlot_SlotMask);
                public MethodClassification Classification => (MethodClassification)(m_wFlags & MethodDescClassification.ClassificationMask);

                public MethodDescChunk* MethodDescChunk
                    => (MethodDescChunk*)(((byte*)Unsafe.AsPointer(ref this)) - ((nuint)sizeof(MethodDescChunk) + (m_chunkIndex * Alignment)));

                public MethodTable* MethodTable => MethodDescChunk->m_methodTable;

                public void* GetMethodEntryPoint()
                {
                    if (HasNonVtableSlot)
                    {
                        var size = GetBaseSize();
                        var pSlot = ((byte*)Unsafe.AsPointer(ref this)) + size;
                        return MethodDescChunk->m_flagsAndTokenRange.Has(V60.MethodDescChunk.Flags.IsZapped)
                            ? new RelativePointer((nint)pSlot).Value
                            : *(void**)pSlot;
                    }

                    return MethodTable->GetSlot(SlotNumber);
                }

                public bool TryAsFCall(out FCallMethodDescPtr md)
                {
                    if (Classification == MethodClassification.FCall)
                    {
                        md = new(Unsafe.AsPointer(ref this), FCallMethodDescPtr.CurrentVtable);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }
                }

                public bool TryAsNDirect(out NDirectMethodDescPtr md)
                {
                    if (Classification == MethodClassification.NDirect)
                    {
                        md = new(Unsafe.AsPointer(ref this), NDirectMethodDescPtr.CurrentVtable);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }
                }

                public bool TryAsEEImpl(out EEImplMethodDescPtr md)
                {
                    if (Classification == MethodClassification.EEImpl)
                    {
                        md = new(Unsafe.AsPointer(ref this), EEImplMethodDescPtr.CurrentVtable);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }
                }

                public bool TryAsArray(out ArrayMethodDescPtr md)
                {
                    if (Classification == MethodClassification.Array)
                    {
                        md = new(Unsafe.AsPointer(ref this), ArrayMethodDescPtr.CurrentVtable);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }
                }

                public bool TryAsInstantiated(out InstantiatedMethodDesc* md)
                {
                    if (Classification == MethodClassification.Instantiated)
                    {
                        md = (InstantiatedMethodDesc*)Unsafe.AsPointer(ref this);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }
                }

                public bool TryAsComPlusCall(out ComPlusCallMethodDesc* md)
                {
                    if (Classification == MethodClassification.ComInterop)
                    {
                        md = (ComPlusCallMethodDesc*)Unsafe.AsPointer(ref this);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }
                }

                public bool TryAsDynamic(out DynamicMethodDescPtr md)
                {
                    if (Classification == MethodClassification.Dynamic)
                    {
                        md = new(Unsafe.AsPointer(ref this), DynamicMethodDescPtr.CurrentVtable);
                        return true;
                    }
                    else
                    {
                        md = default;
                        return false;
                    }

                }

                private static readonly nuint[] s_ClassificationSizeTable = new nuint[] {
                    (nuint) sizeof(MethodDesc),
                    (nuint) FCallMethodDescPtr.CurrentSize,
                    (nuint) NDirectMethodDescPtr.CurrentSize,
                    (nuint) EEImplMethodDescPtr.CurrentSize,
                    (nuint) ArrayMethodDescPtr.CurrentSize,
                    (nuint) sizeof(InstantiatedMethodDesc),
                    (nuint) sizeof(ComPlusCallMethodDesc),
                    (nuint) DynamicMethodDescPtr.CurrentSize,

                    // this table also has a bunch of sizes it uses for fast size lookups, but for us, pregenerating that table is a *mess*
                };

                public nuint SizeOf(bool includeNonVtable = true, bool includeMethodImpl = true, bool includeComPlus = true, bool includeNativeCode = true)
                {
                    var size = GetBaseSize()
                        // All of the extra fields are just one pointer size
                        + (includeNonVtable && m_wFlags.Has(MethodDescClassification.HasNonVtableSlot) ? (nuint)sizeof(void*) : 0)
                        + (includeMethodImpl && m_wFlags.Has(MethodDescClassification.MethodImpl) ? (nuint)sizeof(void*) * 2 : 0)
                        + (includeComPlus && m_wFlags.Has(MethodDescClassification.HasComPlusCallInfo) ? (nuint)sizeof(void*) : 0)
                        + (includeNativeCode && m_wFlags.Has(MethodDescClassification.HasNativeCodeSlot) ? (nuint)sizeof(void*) : 0);

                    //#ifdef FEATURE_PREJIT
                    if (includeNativeCode && HasNativeCodeSlot)
                    {
                        size += ((nuint)GetAddrOfNativeCodeSlot() & 1u) != 0 ? (nuint)sizeof(void*) : 0;
                    }
                    //#endif

                    return size;
                }

                public void* GetNativeCode()
                {
                    if (HasNativeCodeSlot)
                    {
                        var pCode = *(void**)((nuint)GetAddrOfNativeCodeSlot() & ~(nuint)1u); // 1u = FIXUP_LIST_MASK
                        /*
                        #ifdef TARGET_ARM
                                if (pCode != NULL)
                                    pCode |= THUMB_CODE;
                        #endif
                        */
                        if (pCode != null)
                            return pCode;
                    }

                    if (!HasStableEntryPoint || HasPrecode)
                        return null;

                    return GetStableEntryPoint();
                }

                public void* GetStableEntryPoint()
                {
                    return GetMethodEntryPoint();
                }

                public bool HasNonVtableSlot => m_wFlags.Has(MethodDescClassification.HasNonVtableSlot);

                public bool HasStableEntryPoint => m_bFlags2.Has(Flags2.HasStableEntryPoint);

                public bool HasPrecode => m_bFlags2.Has(Flags2.HasPrecode);

                public bool HasNativeCodeSlot => m_wFlags.Has(MethodDescClassification.HasNativeCodeSlot);

                public bool IsUnboxingStub => m_bFlags2.Has(Flags2.IsUnboxingStub);

                public bool HasMethodInstantiation => TryAsInstantiated(out var inst) && inst->IMD_HasMethodInstantiation;
                public bool IsGenericMethodDefinition => TryAsInstantiated(out var inst) && inst->IMD_IsGenericMethodDefinition;
                public bool IsInstantiatingStub
                    => !IsUnboxingStub && TryAsInstantiated(out var inst) && inst->IMD_IsWrapperStubWithInstantiations;

                public bool IsWrapperStub => IsUnboxingStub || IsInstantiatingStub;

                public bool IsTightlyBoundToMethodTable
                {
                    get
                    {
                        if (!HasNonVtableSlot)
                        {
                            return true;
                        }

                        if (HasMethodInstantiation)
                        {
                            return IsGenericMethodDefinition;
                        }

                        if (IsWrapperStub)
                        {
                            return false;
                        }

                        return true;
                    }
                }

                // https://github.com/dotnet/runtime/blob/v6.0.5/src/coreclr/vm/genmeth.cpp#L151
                public static MethodDesc* FindTightlyBoundWrappedMethodDesc(MethodDesc* pMD)
                {
                    if (pMD->IsUnboxingStub && pMD->TryAsInstantiated(out var inst))
                        pMD = inst->IMD_GetWrappedMethodDesc();

                    // this may not actually be necessary for any of the MDs we see, so we'll leave it in its incomplete state
                    // until it actually proves to be an issue
                    if (!pMD->IsTightlyBoundToMethodTable)
                        pMD = pMD->GetCanonicalMethodTable()->GetParallelMethodDesc(pMD);
                    Helpers.DAssert(pMD->IsTightlyBoundToMethodTable);

                    if (pMD->IsUnboxingStub)
                    {
                        pMD = GetNextIntroducedMethod(pMD);
                    }
                    Helpers.DAssert(!pMD->IsUnboxingStub);

                    return pMD;
                }

                public static MethodDesc* GetNextIntroducedMethod(MethodDesc* pMD)
                {
                    var pChunk = pMD->MethodDescChunk;

                    var pNext = (nuint)pMD + pMD->SizeOf();
                    var pEnd = (nuint)pChunk + pChunk->SizeOf;

                    if (pNext < pEnd)
                    {
                        return (MethodDesc*)pNext;
                    }
                    else
                    {
                        Helpers.DAssert(pNext == pEnd);

                        pChunk = pChunk->m_next;
                        if (pChunk is not null)
                        {
                            return pChunk->FirstMethodDesc;
                        }
                        else
                        {
                            return null;
                        }
                    }
                }

                public MethodTable* GetCanonicalMethodTable() => MethodTable->GetCanonicalMethodTable();

                public void* GetAddrOfNativeCodeSlot()
                {
                    var size = SizeOf(includeComPlus: false, includeNativeCode: false);
                    return Unsafe.AsPointer(ref Unsafe.AddByteOffset(ref this, size));
                }

                public nuint GetBaseSize() => GetBaseSize(Classification);

                public static nuint GetBaseSize(MethodClassification classification)
                {
                    return s_ClassificationSizeTable[(int)classification];
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MethodDescChunk
            {
                [Flags]
                public enum Flags : ushort
                {
                    TokenRangeMask = 0x03FF,
                    HasCompactEntrypoints = 0x4000,
                    IsZapped = 0x8000,
                }

                // These are RelativePointer and RelativeFixupPointers in .NET 6 NGEN/Zap (and presumably earlier), but not in .NET 7
                public MethodTable* m_methodTable;
                public MethodDescChunk* m_next;
                public byte m_size; // size of the chunk - 1 (in multiples of MethodDesc::ALIGNMENT)
                public byte m_count; // Number of MethodDescs in this chunk - 1
                public Flags m_flagsAndTokenRange;
                // this is followed by an array of MethodDescs

                public MethodDesc* FirstMethodDesc => (MethodDesc*)((byte*)Unsafe.AsPointer(ref this) + sizeof(MethodDescChunk));
                public uint Size => m_size + 1u;
                public uint Count => m_count + 1u;
                public nuint SizeOf => (nuint)sizeof(MethodDescChunk) + (Size * MethodDesc.Alignment);
            }

            [Attributes.FatInterface]
            public partial struct StoredSigMethodDescPtr
            {
                public static IntPtr[] CurrentVtable { get; }
                    = IntPtr.Size == 8 ? StoredSigMethodDesc_64.FatVtable_ : StoredSigMethodDesc_32.FatVtable_;
                public static int CurrentSize { get; }
                    = IntPtr.Size == 8 ? sizeof(StoredSigMethodDesc_64) : sizeof(StoredSigMethodDesc_32);

                private partial void* GetPSig();
                public void* m_pSig
                {
                    [Attributes.FatInterfaceIgnore]
                    get => GetPSig();
                }
                private partial uint GetCSig();
                public uint m_cSig
                {
                    [Attributes.FatInterfaceIgnore]
                    get => GetCSig();
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(StoredSigMethodDescPtr))]
            public partial struct StoredSigMethodDesc_64
            {
                public MethodDesc @base;
                public void* m_pSig;
                public uint m_cSig;

                // THIS ONLY EXISTS IN 64-BIT
                public uint m_dwExtendedFlags;

                // StoredSigMethodDescPtr impl
                private void* GetPSig() => m_pSig;
                private uint GetCSig() => m_cSig;
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(StoredSigMethodDescPtr))]
            public partial struct StoredSigMethodDesc_32
            {
                public MethodDesc @base;
                public void* m_pSig;
                public uint m_cSig;

                // THIS ONLY EXISTS IN 64-BIT
                //public uint m_dwExtendedFlags;

                // StoredSigMethodDescPtr impl
                private void* GetPSig() => m_pSig;
                private uint GetCSig() => m_cSig;
            }

            [Attributes.FatInterface]
            public partial struct FCallMethodDescPtr
            {
                public static IntPtr[] CurrentVtable { get; }
                    = IntPtr.Size == 8 ? FCallMethodDesc_64.FatVtable_ : FCallMethodDesc_32.FatVtable_;
                public static int CurrentSize { get; }
                    = IntPtr.Size == 8 ? sizeof(FCallMethodDesc_64) : sizeof(FCallMethodDesc_32);

                private partial uint GetECallID();
                public uint m_dwECallID
                {
                    [Attributes.FatInterfaceIgnore]
                    get => GetECallID();
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(FCallMethodDescPtr))]
            public partial struct FCallMethodDesc_64
            {
                public MethodDesc @base;
                public uint m_dwECallID;

                // THIS ONLY EXISTS IN 64-BIT
                public uint m_padding;

                private uint GetECallID() => m_dwECallID;
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(FCallMethodDescPtr))]
            public partial struct FCallMethodDesc_32
            {
                public MethodDesc @base;
                public uint m_dwECallID;

                // THIS ONLY EXISTS IN 64-BIT
                //public uint m_padding;

                private uint GetECallID() => m_dwECallID;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct DynamicResolver { }

            [Flags]
            public enum DynamicMethodDesc_ExtendedFlags
            {
                Attrs = 0x0000FFFF,
                ILStubAttrs = 0x0010 | 0x0007, // mdStatic | mdMemberAccessMask

                MemberAccessMask = 0x0007,
                ReverseStub = 0x0008,
                Static = 0x0010,
                CALLIStub = 0x0020,
                DelegateStub = 0x0040,
                StructMarshalStub = 0x0080,
                Unbreakable = 0x0100,

                SignatureNeedsResture = 0x0400,
                StubNeedsCOMStarted = 0x0800,
                MulticastStub = 0x1000,
                UnboxingILStub = 0x2000,
                WrapperDelegateStub = 0x4000,
                UnmanagedCallersOnlyStub = 0x8000,

                ILStub = 0x00010000,
                LCGMethod = 0x00020000,
                StackArgSize = 0xFFC0000, // native stack arg size for IL stubs
            }

            [Attributes.FatInterface]
            public partial struct DynamicMethodDescPtr
            {
                public static IntPtr[] CurrentVtable { get; }
                    = IntPtr.Size == 8 ? DynamicMethodDesc_64.FatVtable_ : DynamicMethodDesc_32.FatVtable_;
                public static int CurrentSize { get; }
                    = IntPtr.Size == 8 ? sizeof(DynamicMethodDesc_64) : sizeof(DynamicMethodDesc_32);

                private partial DynamicMethodDesc_ExtendedFlags GetFlags();
                public DynamicMethodDesc_ExtendedFlags Flags => GetFlags();
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(DynamicMethodDescPtr))]
            public partial struct DynamicMethodDesc_64
            {
                public StoredSigMethodDesc_64 @base;
                public byte* m_pszMethodName; // PTR_CUTF8
                public DynamicResolver* m_pResolver;

                // THIS ONLY EXISTS IN 32-BIT
                //public uint m_dwExtendedFlags;

                private DynamicMethodDesc_ExtendedFlags GetFlags() => (DynamicMethodDesc_ExtendedFlags)@base.m_dwExtendedFlags;
                public DynamicMethodDesc_ExtendedFlags Flags => GetFlags();
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(DynamicMethodDescPtr))]
            public partial struct DynamicMethodDesc_32
            {
                public StoredSigMethodDesc_32 @base;
                public byte* m_pszMethodName; // PTR_CUTF8
                public DynamicResolver* m_pResolver;

                // THIS ONLY EXISTS IN 32-BIT
                public uint m_dwExtendedFlags;

                private DynamicMethodDesc_ExtendedFlags GetFlags() => (DynamicMethodDesc_ExtendedFlags)m_dwExtendedFlags;
                public DynamicMethodDesc_ExtendedFlags Flags => GetFlags();
            }

            [Attributes.FatInterface]
            public partial struct ArrayMethodDescPtr
            {
                public static IntPtr[] CurrentVtable { get; }
                    = IntPtr.Size == 8 ? ArrayMethodDesc_64.FatVtable_ : ArrayMethodDesc_32.FatVtable_;
                public static int CurrentSize { get; }
                    = IntPtr.Size == 8 ? sizeof(ArrayMethodDesc_64) : sizeof(ArrayMethodDesc_32);
            }

            public enum ArrayFunc
            {
                Get = 0,
                Set = 1,
                Address = 2,
                Ctor = 3,
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(ArrayMethodDescPtr))]
            public partial struct ArrayMethodDesc_64
            {
                public StoredSigMethodDesc_64 @base;
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(ArrayMethodDescPtr))]
            public partial struct ArrayMethodDesc_32
            {
                public StoredSigMethodDesc_32 @base;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct NDirectWriteableData { }

            [Flags]
            public enum NDirectMethodDesc_Flags : ushort
            {
                // Init group
                EarlyBound = 0x0001,
                HasSuppressUnmanagedCodeAccess = 0x0002,
                DefaultDllImportSearchPathIsCached = 0x0004,
                // runtime group
                IsMarshalingRequiredCached = 0x0010,
                CachedMarshalingRequired = 0x0020,
                NativeAnsi = 0x0040,
                LastError = 0x0080,
                NativeNoMangle = 0x0100,
                VarArgs = 0x0200,
                StdCall = 0x0400,
                ThisCall = 0x0800,
                IsQCall = 0x1000,
                DefaultDllImportSearchPathsStatus = 0x2000,
                NDirectPopulated = 0x8000,
            }

            [Attributes.FatInterface]
            public partial struct NDirectMethodDescPtr
            {
                public static IntPtr[] CurrentVtable { get; }
                    = PlatformDetection.Architecture == ArchitectureKind.x86
                        ? NDirectMethodDesc_x86.FatVtable_
                        : NDirectMethodDesc_other.FatVtable_;
                public static int CurrentSize { get; }
                    = PlatformDetection.Architecture == ArchitectureKind.x86
                        ? sizeof(NDirectMethodDesc_x86)
                        : sizeof(NDirectMethodDesc_other);
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(NDirectMethodDescPtr))]
            public partial struct NDirectMethodDesc_other
            {
                public MethodDesc @base;

                [StructLayout(LayoutKind.Sequential)]
                public struct NDirect
                {
                    public void* m_pNativeNDirectTarget;
                    public byte* m_pszEntrypointName; // PTR_CUTF8
                    public nuint union_pszLibName_dwECallID;
                    public NDirectWriteableData* m_pWriteableData;
                    public void* m_pImportThunkGlue;
                    public uint m_DefaultDllImportSearchPathsAttributeValue; // ULONG
                    public NDirectMethodDesc_Flags m_wFlags;
                    // THIS ONLY EXISTS ON X86
                    //public ushort m_cbStackArgumentSize;
                    public MethodDesc* m_pStubMD;
                }

                NDirect ndirect;
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(NDirectMethodDescPtr))]
            public partial struct NDirectMethodDesc_x86
            {
                public MethodDesc @base;

                [StructLayout(LayoutKind.Sequential)]
                public struct NDirect
                {
                    public void* m_pNativeNDirectTarget;
                    public byte* m_pszEntrypointName; // PTR_CUTF8
                    public nuint union_pszLibName_dwECallID;
                    public NDirectWriteableData* m_pWriteableData;
                    public void* m_pImportThunkGlue;
                    public uint m_DefaultDllImportSearchPathsAttributeValue; // ULONG
                    public NDirectMethodDesc_Flags m_wFlags;
                    // THIS ONLY EXISTS ON X86
                    public ushort m_cbStackArgumentSize;
                    public MethodDesc* m_pStubMD;
                }

                NDirect ndirect;
            }

            [Attributes.FatInterface]
            public partial struct EEImplMethodDescPtr
            {
                public static IntPtr[] CurrentVtable { get; }
                    = IntPtr.Size == 8 ? EEImplMethodDesc_64.FatVtable_ : EEImplMethodDesc_32.FatVtable_;
                public static int CurrentSize { get; }
                    = IntPtr.Size == 8 ? sizeof(EEImplMethodDesc_64) : sizeof(EEImplMethodDesc_32);
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(EEImplMethodDescPtr))]
            public partial struct EEImplMethodDesc_64
            {
                public StoredSigMethodDesc_64 @base;
            }

            [StructLayout(LayoutKind.Sequential)]
            [Attributes.FatInterfaceImpl(typeof(EEImplMethodDescPtr))]
            public partial struct EEImplMethodDesc_32
            {
                public StoredSigMethodDesc_32 @base;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct ComPlusCallMethodDesc
            {
                public MethodDesc @base;
                public void* m_pComPlusCallInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct InstantiatedMethodDesc
            {
                public MethodDesc @base;

                [Flags]
                public enum Flags : ushort
                {
                    KindMask = 0x07,
                    GenericMethodDefinition = 0x00,
                    UnsharedMethodInstantiation = 0x01,
                    SharedMethodInstantiation = 0x02,
                    WrapperStubWithInstantiations = 0x03,

                    EnCAddedMethod = 0x07,
                    Unrestored = 0x08,
                    HasComPlusCallInfo = 0x10,
                }

                // pDictLayout for SharedMethodInstantiation
                // pWrappedMethodDesc for WrapperStubWithInstantiations
                public void* union_pDictLayout_pWrappedMethodDesc;

                // Type parameters to method (exact)
                // For non-unboxing instantiating stubs this is actually
                // a dictionary and further slots may hang off the end of the
                // instantiation.
                //
                // For generic method definitions that are not the typical method definition (e.g. C<int>.m<U>)
                // this field is null; to obtain the instantiation use LoadMethodInstantiation
                public Dictionary* m_pPerInstInfo; // SHARED

                public Flags m_wFlags2;
                public ushort m_wNumGenericArgs;

                public bool IMD_HasMethodInstantiation => IMD_IsGenericMethodDefinition ? true : m_pPerInstInfo != null;
                public bool IMD_IsGenericMethodDefinition => (m_wFlags2 & Flags.KindMask) == Flags.GenericMethodDefinition;
                public bool IMD_IsWrapperStubWithInstantiations => (m_wFlags2 & Flags.KindMask) == Flags.WrapperStubWithInstantiations;

                public MethodDesc* IMD_GetWrappedMethodDesc()
                {
                    Helpers.Assert(IMD_IsWrapperStubWithInstantiations);
                    return (MethodDesc*)union_pDictLayout_pWrappedMethodDesc;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Dictionary
            {
                // TODO: impl
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Module { }
            [StructLayout(LayoutKind.Sequential)]
            public struct MethodTableWriteableData { }

            [StructLayout(LayoutKind.Sequential)]
            public struct VTableIndir2_t
            {
                public void* pCode;
                public void* Value => pCode;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct VTableIndir_t
            {
                public VTableIndir2_t* Value;
            }

            private static class MultipurposeSlotHelpers
            {
                public static byte OffsetOfMp1()
                {
                    MethodTable t = default;
                    return (byte)((byte*)&t.union_pPerInstInfo_ElementTypeHnd_pMultipurposeSlot1 - (byte*)&t);
                }
                public static byte OffsetOfMp2()
                {
                    MethodTable t = default;
                    return (byte)((byte*)&t.union_p_InterfaceMap_pMultipurposeSlot2 - (byte*)&t);
                }
                public static byte RegularOffset(int index)
                {
                    return (byte)(sizeof(MethodTable) + index * IntPtr.Size - 2 * IntPtr.Size);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public partial struct MethodTable
            {

                public uint m_dwFlags;
                public uint m_BaseSize;

                [Flags]
                public enum Flags2 : ushort
                {
                    MultipurposeSlotsMask = 0x001F,
                    HasPerInstInfo = 0x0001,
                    HasInterfaceMap = 0x0002,
                    HasDispatchMapSlot = 0x0004,
                    HasNonVirtualSlots = 0x0008,
                    HasModuleOverride = 0x0010,
                    IsZapped = 0x0020,
                    IsPreRestored = 0x0040,
                    HasModuleDependencies = 0x0080,
                    IsIntrinsicType = 0x0100,
                    RequiresDispatchTokenFat = 0x0200,
                    HasCctor = 0x0400,
                    HasVirtualStaticMethods = 0x0800,
                    REquiresAlign8 = 0x1000,
                    HasBoxedRegularStatics = 0x2000,
                    HasSingleNonVirtualSlot = 0x4000,
                    DependsOnEquivalentOrForwardedStructs = 0x8000,
                }

                public Flags2 m_wFlags2;
                public ushort m_wToken;
                public ushort m_wNumVirtuals;
                public ushort m_wNumInterfaces;
                // LPCUTF8 debug_m_szClassName; // only in _DEBUG
                private void* m_pParentMethodTable; // this is actually ParentMT_t, which is a RelativeFixupPointer on Linux ARM, and a regular pointer everywhere else
                public Module* m_pLoaderModule;
                public MethodTableWriteableData* m_pWriteableData;

                public enum UnionLowBits
                {
                    EEClass = 0, // ptr to EEClass, making this MT the canonical MT
                    Invalid = 1, // Unused.
                    MethodTable = 2, // ptr to canonical MT
                    Indirection = 3, // ptr to indirection cell pointing to canonical MT (only used with FEATURE_PREJIT)
                }
                public void* union_pEEClass_pCanonMT;

                public void* union_pPerInstInfo_ElementTypeHnd_pMultipurposeSlot1;
                public void* union_p_InterfaceMap_pMultipurposeSlot2;

                // then vtable/nonvirutal slots, overflow multipurpose slots, optional members, generic dict pointers, interface map, generic inst dict

                public MethodTable* GetCanonicalMethodTable()
                {
                    var addr = (nuint)union_pEEClass_pCanonMT;
                    if ((addr & 2) == 0)
                        return (MethodTable*)addr;
                    if ((addr & 1) != 0)
                        return *(MethodTable**)(addr - 3);
                    return (MethodTable*)(addr - 2);
                }

                public MethodDesc* GetParallelMethodDesc(MethodDesc* pDefMD)
                {
                    return GetMethodDescForSlot(pDefMD->SlotNumber);
                }

                // enum_flag_Category_Mask == enum_flag_Category_Interface
                public bool IsInterface => (m_dwFlags & 0x000F0000) == 0x000C0000;

                public MethodDesc* GetMethodDescForSlot(uint slotNumber)
                {
                    //var pCode = GetRestoredSlot(slotNumber);

                    if (IsInterface && slotNumber < GetNumVirtuals())
                    {
                        // TODO: MethodDesc::GetMethodDescFromStubAddr
                    }

                    // TODO: ExecutionManager::GetCodeMethodDesc, which calls EECodeInfo::Init, which calls a bunch of other stuff
                    throw new NotImplementedException();
                }

                public void* GetRestoredSlot(uint slotNumber)
                {
                    var pMT = (MethodTable*)Unsafe.AsPointer(ref this);

                    while (true)
                    {
                        pMT = pMT->GetCanonicalMethodTable();
                        Helpers.DAssert(pMT is not null);

                        var slot = pMT->GetSlot(slotNumber);

                        if (slot != null // I'm still not sure if FEATURE_PREJIT is set for our stuff
                        /*#ifdef FEATURE_PREJIT
                                    && !pMT->GetLoaderModule()->IsVirtualImportThunk(slot)
                        #endif*/
                        )
                        {
                            return slot;
                        }

                        pMT = pMT->GetParentMethodTable();
                    }
                }

                public bool HasIndirectParent => (m_dwFlags & 0x00800000) != 0; // enum_flag_HasIndirectParent

                public MethodTable* GetParentMethodTable()
                {
                    var ptr = m_pParentMethodTable;
                    // TODO: RelativeFixupPointer when needed
                    if (HasIndirectParent)
                    {
                        return *(MethodTable**)ptr; // I'm not sure if this is actually correct
                    }
                    else
                    {
                        return (MethodTable*)ptr;
                    }
                }

                public void* GetSlot(uint slotNumber)
                {
                    var pSlot = GetSlotPtrRaw(slotNumber);
                    if (slotNumber < GetNumVirtuals())
                    {
                        return ((VTableIndir2_t*)pSlot)->Value;
                    }
                    else if ((m_wFlags2 & Flags2.IsZapped) != 0 && slotNumber >= GetNumVirtuals())
                    {
                        // Non-virtual slots in NGened images are relative pointers
                        return ((RelativePointer*)pSlot)->Value;
                    }
                    else
                    {
                        return *(void**)pSlot;
                    }
                }

                public nint GetSlotPtrRaw(uint slotNum)
                {
                    if (slotNum < GetNumVirtuals())
                    {
                        var index = GetIndexOfVtableIndirection(slotNum);
                        var @base = (nint)(&(GetVtableIndirections()[index]));
                        var baseAfterInd = VTableIndir_t__GetValueMaybeNullAtPtr(@base) + GetIndexAfterVtableIndirection(slotNum);
                        return (nint)baseAfterInd;
                    }
                    else if (HasSingleNonVirtualSlot)
                    {
                        return GetNonVirtualSlotsPtr();
                    }
                    else
                    {
                        return (nint)(GetNonVirtualSlotsArray() + (slotNum - GetNumVirtuals()));
                    }
                }

                public ushort GetNumVirtuals()
                {
                    return m_wNumVirtuals;
                }

                public const int VTABLE_SLOTS_PER_CHUNK = 8;
                public const int VTABLE_SLOTS_PER_CHUNK_LOG2 = 3;

                public static uint GetIndexOfVtableIndirection(uint slotNum)
                {
                    return slotNum >> VTABLE_SLOTS_PER_CHUNK_LOG2;
                }

                public VTableIndir_t* GetVtableIndirections()
                {
                    return (VTableIndir_t*)((byte*)Unsafe.AsPointer(ref this) + sizeof(MethodTable));
                }

                public static VTableIndir2_t* VTableIndir_t__GetValueMaybeNullAtPtr(nint @base)
                {
                    // we assume for now that VTableIndir_t doesn't use RElativePointer because it depends on FEATURE_NGEN_RELOCS_OPTIMIZATION
                    return (VTableIndir2_t*)@base;
                }

                public static uint GetIndexAfterVtableIndirection(uint slotNum)
                {
                    return slotNum & (VTABLE_SLOTS_PER_CHUNK - 1);
                }

                public bool HasSingleNonVirtualSlot => m_wFlags2.Has(Flags2.HasSingleNonVirtualSlot);

                // https://github.com/dotnet/runtime/blob/v6.0.5/src/coreclr/vm/methodtable.cpp#L318
                [Attributes.MultipurposeSlotOffsetTable(3, typeof(MultipurposeSlotHelpers))]
                private static partial byte[] GetNonVirtualSlotsOffsets();

                private static readonly byte[] c_NonVirtualSlotsOffsets = GetNonVirtualSlotsOffsets();

                public nint GetNonVirtualSlotsPtr()
                {
                    return GetMultipurposeSlotPtr(Flags2.HasNonVirtualSlots, c_NonVirtualSlotsOffsets);
                }

                public nint GetMultipurposeSlotPtr(Flags2 flag, byte[] offsets)
                {
                    nint offset = offsets[(ushort)m_wFlags2 & ((ushort)flag - 1)];
                    if (offset >= sizeof(MethodTable))
                    {
                        offset += (nint)GetNumVTableIndirections() * sizeof(VTableIndir_t);
                    }
                    return (nint)Unsafe.AsPointer(ref this) + offset;
                }

                public void*** GetNonVirtualSlotsArray()
                {
                    return (void***)GetNonVirtualSlotsPtr();
                }

                public uint GetNumVTableIndirections()
                {
                    return GetNumVtableIndirections(GetNumVirtuals());
                }
                public static uint GetNumVtableIndirections(uint numVirtuals)
                {
                    return (numVirtuals + (VTABLE_SLOTS_PER_CHUNK - 1)) >> VTABLE_SLOTS_PER_CHUNK_LOG2;
                }
            }
        }
    }
}
