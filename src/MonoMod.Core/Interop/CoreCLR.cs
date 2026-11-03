using System;

namespace MonoMod.Core.Interop
{
    internal static unsafe partial class CoreCLR
    {
        public enum CorJitResult
        {
            CORJIT_OK = 0,
            CORJIT_BADCODE = unchecked((int)0x80000001),
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

        public readonly struct InvokeGetJitFlagsPtr
        {
            private readonly IntPtr methodPtr;
            public InvokeGetJitFlagsPtr(
                delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    void*,  // CORJIT_FLAGS* flags
                    uint,   // DWORD sizeInBytes
                    uint    // result DWORD, num bytes written to flags
                > ptr
            )
            {
                methodPtr = (IntPtr)ptr;
            }

            public delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    void*,  // CORJIT_FLAGS* flags
                    uint,   // DWORD sizeInBytes
                    uint    // result DWORD, num bytes written to flags
                > InvokeGetJitFlags
                => (delegate*<
                    IntPtr, // method
                    IntPtr, // ICorJitInfo* this
                    void*,  // CORJIT_FLAGS* flags
                    uint,   // DWORD sizeInBytes
                    uint    // result DWORD, num bytes written to flags
                >)methodPtr;
        }
    }
}
