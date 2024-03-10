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
        [SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes",
            Justification = "It must be non-static to be able to inherit others, as it does. This allows the Core*Runtime types " +
            "to each reference exactly the version they represent, and the compiler automatically resolves the correct one without " +
            "needing duplicates.")]
        public partial class V60 : V50
        {
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
