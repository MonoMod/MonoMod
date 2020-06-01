using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNETCore31Platform : DetourRuntimeNETCorePlatform {
        public static readonly Guid JitVersionGuid = new Guid("d609bed1-7831-49fc-bd49-b6f054dd4d46");

        protected override unsafe void DisableInlining(MethodBase method, RuntimeMethodHandle handle) {
            // https://github.com/dotnet/runtime/blob/89965be3ad2be404dc82bd9e688d5dd2a04bcb5f/src/coreclr/src/vm/method.hpp#L178
            // mdcNotInline = 0x2000
            // References to RuntimeMethodHandle (CORINFO_METHOD_HANDLE) pointing to MethodDesc
            // can be traced as far back as https://ntcore.com/files/netint_injection.htm

            const int offset =
                2 // UINT16 m_wFlags3AndTokenRemainder
              + 1 // BYTE m_chunkIndex
              + 1 // BYTE m_chunkIndex
              + 2 // WORD m_wSlotNumber
              ;
            ushort* m_wFlags = (ushort*) (((byte*) handle.Value) + offset);
            *m_wFlags |= 0x2000;
        }

        private d_compileMethod GetCompileMethod(IntPtr jit)
            => ReadObjectVTable(jit, VTableIndex_ICorJitCompiler_compileMethod).AsDelegate<d_compileMethod>();

        private unsafe d_compileMethod our_compileMethod;
        private IntPtr real_compileMethodPtr;
        private d_compileMethod real_compileMethod;

        protected override unsafe void InstallJitHooks(IntPtr jit) {
            SetupJitHookHelpers();
            real_compileMethod = GetCompileMethod(jit);

            our_compileMethod = CompileMethodHook;
            IntPtr our_compileMethodPtr = Marshal.GetFunctionPointerForDelegate(our_compileMethod);

            // Create a native trampoline to pre-JIT the hook itself
            {
                NativeDetourData trampolineData = CreateNativeTrampolineTo(our_compileMethodPtr);
                d_compileMethod trampoline = trampolineData.Method.AsDelegate<d_compileMethod>();
                trampoline(IntPtr.Zero, IntPtr.Zero, new CORINFO_METHOD_INFO(), 0, out _, out _);
                FreeNativeTrampoline(trampolineData);
            }

            // Install the JIT hook
            IntPtr* vtableEntry = GetVTableEntry(jit, VTableIndex_ICorJitCompiler_compileMethod);
            DetourHelper.Native.MakeWritable((IntPtr) vtableEntry, (uint)IntPtr.Size);
            real_compileMethodPtr = *vtableEntry;
            *vtableEntry = our_compileMethodPtr;
        }

        private static NativeDetourData CreateNativeTrampolineTo(IntPtr target) {
            IntPtr mem = DetourHelper.Native.MemAlloc(64); // 64 bytes should be enough on all platforms
            NativeDetourData data = DetourHelper.Native.Create(mem, target);
            DetourHelper.Native.MakeWritable(data);
            DetourHelper.Native.Apply(data);
            DetourHelper.Native.MakeExecutable(data);
            DetourHelper.Native.FlushICache(data);
            return data;
        }

        private static void FreeNativeTrampoline(NativeDetourData data) {
            DetourHelper.Native.MakeWritable(data);
            DetourHelper.Native.MemFree(data.Method);
            DetourHelper.Native.Free(data);
        }

        private enum CorJitResult {
            CORJIT_OK = 0,
            // There are more, but I don't particularly care about them
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct CORINFO_SIG_INST {
            public uint classInstCount;
            public IntPtr* classInst; // CORINFO_CLASS_HANDLE* // (representative, not exact) instantiation for class type variables in signature
            public uint methInstCount;
            public IntPtr* methInst; // CORINFO_CLASS_HANDLE* // (representative, not exact) instantiation for method type variables in signature
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CORINFO_SIG_INFO {
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
        private unsafe struct CORINFO_METHOD_INFO {
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
        private unsafe delegate CorJitResult d_compileMethod(
            IntPtr thisPtr, // ICorJitCompiler*
            IntPtr corJitInfo, // ICorJitInfo*
            in CORINFO_METHOD_INFO methodInfo, // CORINFO_METHOD_INFO*
            uint flags,
            out byte* nativeEntry,
            out ulong nativeSizeOfCode
        );

        [ThreadStatic]
        private static int hookEntrancy = 0;
        private unsafe CorJitResult CompileMethodHook(
            IntPtr jit, // ICorJitCompiler*
            IntPtr corJitInfo, // ICorJitInfo*
            in CORINFO_METHOD_INFO methodInfo, // CORINFO_METHOD_INFO*
            uint flags, 
            out byte* nativeEntry, 
            out ulong nativeSizeOfCode) {

            nativeEntry = null;
            nativeSizeOfCode = 0;

            if (jit == IntPtr.Zero)
                return CorJitResult.CORJIT_OK;

            hookEntrancy++;
            try {
                if (hookEntrancy == 1) {
                    // This is the top level JIT entry point, do our custom stuff
                    CorJitResult result = real_compileMethod.Invoke(jit, corJitInfo, methodInfo, flags, out nativeEntry, out nativeSizeOfCode);

                    JitHookCore(CreateHandleForHandlePointer(methodInfo.ftn), (IntPtr) nativeEntry, nativeSizeOfCode, null, null);

                    return result;
                } else {
                    return real_compileMethod.Invoke(jit, corJitInfo, methodInfo, flags, out nativeEntry, out nativeSizeOfCode);
                }
            } catch {
                // eat the exception so we don't accidentally bubble up to native code
                return CorJitResult.CORJIT_OK;
            } finally {
                hookEntrancy--;
            }
        }

        private delegate object d_MethodHandle_GetLoaderAllocator(IntPtr methodHandle);
        private delegate object d_CreateRuntimeMethodInfoStub(IntPtr methodHandle, object loaderAllocator);
        private delegate RuntimeMethodHandle d_CreateRuntimeMethodHandle(object runtimeMethodInfo);

        protected static RuntimeMethodHandle CreateHandleForHandlePointer(IntPtr handle)
            => CreateRuntimeMethodHandle(CreateRuntimeMethodInfoStub(handle, MethodHandle_GetLoaderAllocator(handle)));

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static object MethodHandle_GetLoaderAllocator(IntPtr methodHandle) {
            _ = methodHandle;
            throw new InvalidOperationException();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static object CreateRuntimeMethodInfoStub(IntPtr methodHandle, object keepalive) {
            _ = methodHandle;
            _ = keepalive;
            throw new InvalidOperationException();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static RuntimeMethodHandle CreateRuntimeMethodHandle(object runtimeMethodInfo) {
            _ = runtimeMethodInfo;
            throw new InvalidOperationException();
        }

        protected virtual void SetupJitHookHelpers() {
            Type genericFunc = typeof(Func<,>);

            { //  set up GetLoaderAllocator
                MethodInfo our_getLoaderAllocator = typeof(DetourRuntimeNETCore31Platform).GetMethod(nameof(MethodHandle_GetLoaderAllocator), BindingFlags.Static | BindingFlags.NonPublic);

                MethodInfo getLoaderAllocator = typeof(RuntimeMethodHandle).GetMethod("GetLoaderAllocator", BindingFlags.Static | BindingFlags.NonPublic);

                HookPermanent(our_getLoaderAllocator, getLoaderAllocator);
            }

            { // set up CreateRuntimeMethodInfoStub
                MethodInfo our_createRuntimeMethodInfoStub = typeof(DetourRuntimeNETCore31Platform).GetMethod(nameof(CreateRuntimeMethodInfoStub), BindingFlags.Static | BindingFlags.NonPublic);

                Type[] runtimeMethodInfoStubCtorArgs = new Type[] { typeof(IntPtr), typeof(object) };
                Type runtimeMethodInfoStub = typeof(RuntimeMethodHandle).Assembly.GetType("System.RuntimeMethodInfoStub");
                ConstructorInfo runtimeMethodInfoStubCtor = runtimeMethodInfoStub.GetConstructor(runtimeMethodInfoStubCtorArgs);

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

                HookPermanent(our_createRuntimeMethodInfoStub, runtimeMethodInfoStubCtorWrapper);
            }

            {
                MethodInfo our_createRuntimeMethodHandle = typeof(DetourRuntimeNETCore31Platform).GetMethod(nameof(CreateRuntimeMethodHandle), BindingFlags.Static | BindingFlags.NonPublic);

                Type iRuntimeMethodInfo = typeof(RuntimeMethodHandle).Assembly.GetType("System.IRuntimeMethodInfo");
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

                HookPermanent(our_createRuntimeMethodHandle, ctorWrapper);
            }
        }

        protected void HookPermanent(MethodBase from, MethodBase to) {
            Pin(from);
            Pin(to);
            HookPermanent(GetNativeStart(from), GetNativeStart(to));
        }
        protected void HookPermanent(IntPtr from, IntPtr to) {
            NativeDetourData detour = DetourHelper.Native.Create(
                from, to, null
            );
            DetourHelper.Native.MakeWritable(detour);
            DetourHelper.Native.Apply(detour);
            DetourHelper.Native.MakeExecutable(detour);
            DetourHelper.Native.FlushICache(detour);
            DetourHelper.Native.Free(detour);
            // No need to undo the detour.
        }
    }
}
