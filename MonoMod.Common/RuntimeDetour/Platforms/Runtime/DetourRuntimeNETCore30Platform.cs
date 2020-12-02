using Mono.Cecil;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MC = Mono.Cecil;
using CIL = Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class DetourRuntimeNETCore30Platform : DetourRuntimeNETCorePlatform {
        // The JitVersionGuid is the same for Core 3.0 and 3.1
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

            // Make sure we run the cctor before the hook to avoid wierdness
            _ = hookEntrancy;

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

                    RuntimeTypeHandle[] genericClassArgs = null;
                    RuntimeTypeHandle[] genericMethodArgs = null;

                    if (methodInfo.args.sigInst.classInst != null) {
                        genericClassArgs = new RuntimeTypeHandle[methodInfo.args.sigInst.classInstCount];
                        for (int i = 0; i < genericClassArgs.Length; i++) {
                            genericClassArgs[i] = GetTypeFromNativeHandle(methodInfo.args.sigInst.classInst[i]).TypeHandle;
                        }
                    }
                    if (methodInfo.args.sigInst.methInst != null) {
                        genericMethodArgs = new RuntimeTypeHandle[methodInfo.args.sigInst.methInstCount];
                        for (int i = 0; i < genericMethodArgs.Length; i++) {
                            genericMethodArgs[i] = GetTypeFromNativeHandle(methodInfo.args.sigInst.methInst[i]).TypeHandle;
                        }
                    }

                    RuntimeTypeHandle declaringType = GetDeclaringTypeOfMethodHandle(methodInfo.ftn).TypeHandle;
                    RuntimeMethodHandle method = CreateHandleForHandlePointer(methodInfo.ftn);

                    JitHookCore(declaringType, method, (IntPtr) nativeEntry, nativeSizeOfCode, genericClassArgs, genericMethodArgs);

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

        protected delegate object d_MethodHandle_GetLoaderAllocator(IntPtr methodHandle);
        protected delegate object d_CreateRuntimeMethodInfoStub(IntPtr methodHandle, object loaderAllocator);
        protected delegate RuntimeMethodHandle d_CreateRuntimeMethodHandle(object runtimeMethodInfo);
        protected delegate Type d_GetDeclaringTypeOfMethodHandle(IntPtr methodHandle);
        protected delegate Type d_GetTypeFromNativeHandle(IntPtr handle);

        protected RuntimeMethodHandle CreateHandleForHandlePointer(IntPtr handle)
            => CreateRuntimeMethodHandle(CreateRuntimeMethodInfoStub(handle, MethodHandle_GetLoaderAllocator(handle)));

        private readonly List<object> keepalive = new List<object>();

        protected d_MethodHandle_GetLoaderAllocator MethodHandle_GetLoaderAllocator;
        protected d_CreateRuntimeMethodInfoStub CreateRuntimeMethodInfoStub;
        protected d_CreateRuntimeMethodHandle CreateRuntimeMethodHandle;
        protected d_GetDeclaringTypeOfMethodHandle GetDeclaringTypeOfMethodHandle;
        protected d_GetTypeFromNativeHandle GetTypeFromNativeHandle;

        protected virtual void SetupJitHookHelpers() {
            Type Unsafe = typeof(object).Assembly.GetType("Internal.Runtime.CompilerServices.Unsafe");
            MethodInfo Unsafe_As = Unsafe.GetMethods().First(m => m.Name == "As" && m.ReturnType.IsByRef);

            const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

            // GetLoaderAllocator should always be present
            { // set up GetLoaderAllocator
                MethodInfo getLoaderAllocator = typeof(RuntimeMethodHandle).GetMethod("GetLoaderAllocator", StaticNonPublic);

                MethodInfo invokeWrapper;
                using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                        "MethodHandle_GetLoaderAllocator", typeof(object), new Type[] { typeof(IntPtr) }
                    )) {
                    ILGenerator il = dmd.GetILGenerator();
                    Type paramType = getLoaderAllocator.GetParameters().First().ParameterType;
                    il.Emit(OpCodes.Ldarga_S, 0);
                    il.Emit(OpCodes.Call, Unsafe_As.MakeGenericMethod(typeof(IntPtr), paramType));
                    il.Emit(OpCodes.Ldobj, paramType);
                    il.Emit(OpCodes.Call, getLoaderAllocator);
                    il.Emit(OpCodes.Ret);

                    invokeWrapper = dmd.Generate();
                }
                // this might not actually work
                MethodHandle_GetLoaderAllocator = invokeWrapper.CreateDelegate<d_MethodHandle_GetLoaderAllocator>();
            }

            { // set up GetTypeFromNativeHandle
                MethodInfo getTypeFromHandleUnsafe = GetOrCreateGetTypeFromHandleUnsafe();
                GetTypeFromNativeHandle = getTypeFromHandleUnsafe.CreateDelegate<d_GetTypeFromNativeHandle>();
            }

            { // set up GetDeclaringTypeOfMethodHandle
                Type methodHandleInternal = typeof(RuntimeMethodHandle).Assembly.GetType("System.RuntimeMethodHandleInternal");
                MethodInfo getDeclaringType = typeof(RuntimeMethodHandle).GetMethod("GetDeclaringType", StaticNonPublic, null, new Type[] { methodHandleInternal }, null);

                MethodInfo invokeWrapper;
                using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                        "GetDeclaringTypeOfMethodHandle", typeof(Type), new Type[] { typeof(IntPtr) }
                    )) {
                    ILGenerator il = dmd.GetILGenerator();
                    il.Emit(OpCodes.Ldarga_S, 0);
                    il.Emit(OpCodes.Call, Unsafe_As.MakeGenericMethod(typeof(IntPtr), methodHandleInternal));
                    il.Emit(OpCodes.Ldobj, methodHandleInternal);
                    il.Emit(OpCodes.Call, getDeclaringType);
                    il.Emit(OpCodes.Ret);

                    invokeWrapper = dmd.Generate();
                }
                // this might not actually work
                GetDeclaringTypeOfMethodHandle = invokeWrapper.CreateDelegate<d_GetDeclaringTypeOfMethodHandle>();
            }

            { // set up CreateRuntimeMethodInfoStub
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

                CreateRuntimeMethodInfoStub = runtimeMethodInfoStubCtorWrapper.CreateDelegate<d_CreateRuntimeMethodInfoStub>();
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

                CreateRuntimeMethodHandle = ctorWrapper.CreateDelegate<d_CreateRuntimeMethodHandle>();
            }
        }

        private MethodInfo _getTypeFromHandleUnsafeMethod;
        private MethodInfo GetOrCreateGetTypeFromHandleUnsafe() {
            if (_getTypeFromHandleUnsafeMethod != null)
                return _getTypeFromHandleUnsafeMethod;

            Assembly assembly;

            const string MethodName = "GetTypeFromHandleUnsafe";

#if !CECIL0_9
            using (
#endif
            ModuleDefinition module = ModuleDefinition.CreateModule(
                "MonoMod.RuntimeDetour.Runtime.NETCore3+Helpers",
                new ModuleParameters() {
                    Kind = ModuleKind.Dll,
                }
            )
#if CECIL0_9
            ;
#else
            )
#endif
            {
                TypeDefinition type = new TypeDefinition(
                    "System",
                    "Type",
                    MC.TypeAttributes.Public | MC.TypeAttributes.Abstract
                ) {
                    BaseType = module.TypeSystem.Object
                };
                module.Types.Add(type);

                MethodDefinition method = new MethodDefinition(
                    MethodName,
                    MC.MethodAttributes.Static | MC.MethodAttributes.Public,
                    module.ImportReference(typeof(Type))
                ) {
                    IsInternalCall = true
                };
                method.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(IntPtr))));
                type.Methods.Add(method);

                assembly = ReflectionHelper.Load(module);
            }

            MakeAssemblySystemAssembly(assembly);

            return _getTypeFromHandleUnsafeMethod = assembly.GetType("System.Type").GetMethod(MethodName);
        }

        protected virtual void MakeAssemblySystemAssembly(Assembly assembly) {
            throw new NotImplementedException();
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
