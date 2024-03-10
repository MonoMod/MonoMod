using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{
    internal static unsafe partial class CoreCLR
    {
        public enum CorJitResult
        {
            CORJIT_OK = 0,
            // There are more, but I don't particularly care about them
        }

        public readonly struct InvokeCompileMethodPtr
        {
            private readonly IntPtr methodPtr;
            public InvokeCompileMethodPtr(
                delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitCompiler*
                    IntPtr, // ICorJitInfo*
                    V21.CORINFO_METHOD_INFO*, // CORINFO_METHOD_INFO*
                    uint,
                    byte**,
                    uint*,
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
                    CorJitResult
                > InvokeCompileMethod
                => (delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitCompiler*
                    IntPtr, // ICorJitInfo*
                    V21.CORINFO_METHOD_INFO*, // CORINFO_METHOD_INFO*
                    uint,
                    byte**,
                    uint*,
                    CorJitResult
                >)methodPtr;
        }

        public class V21
        {
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
        }

        [SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes",
            Justification = "It must be non-static to be able to inherit others, as it does. This allows the Core*Runtime types " +
            "to each reference exactly the version they represent, and the compiler automatically resolves the correct one without " +
            "needing duplicates.")]
        public class V30 : V21 { }

        public class V31 : V30 { }

        public class V50 : V31 { }
    }
}
