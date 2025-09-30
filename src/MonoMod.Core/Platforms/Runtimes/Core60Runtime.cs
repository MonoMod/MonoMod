using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;


#if NET6_USE_RUNTIME_INTROSPECTION
using System.Reflection;
using System.Runtime.CompilerServices;
#endif
using static MonoMod.Core.Interop.CoreCLR;
using static MonoMod.Core.Interop.CoreCLR.V60;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core60Runtime : Core50Runtime
    {
        private readonly IArchitecture arch;

        public Core60Runtime(ISystem system, IArchitecture arch) : base(system)
        {
            this.arch = arch;
        }

        // src/coreclr/inc/jiteeversionguid.h line 46
        // 5ed35c58-857b-48dd-a818-7c0136dc9f73
        private static readonly Guid JitVersionGuid = new Guid(
            0x5ed35c58,
            0x857b,
            0x48dd,
            0xa8, 0x18, 0x7c, 0x01, 0x36, 0xdc, 0x9f, 0x73
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        // As a part of .NET 6's W^X support, the JIT doesn't actually write its code directly to the output.
        // Instead it's copied in to the location that it passes as an out parameter *after* the JIT returns. 
        // Therefore, in order to have our patches applied correctly, we need to poke into the CEEInto parameter
        // to find the address of the RW code and write to that instead.

        // It may actually be easier to wrap the CEEInfo we pass to the actual JIT so we can intercept allocMem
        // and get the actual writable regions form that call.

        // class ICorDynamicInfo : public ICorStaticInfo
        // class ICorJitInfo : public ICorDynamicInfo
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        protected unsafe struct ICorJitInfoWrapper
        {
            public IntPtr Vtbl;
            public IntPtr** Wrapped;

            public const int HotCodeRW = 0;
            public const int ColdCodeRW = 1;
            // the other 2-6 are left unused

            private const int DataQWords = 4;
            private fixed ulong data[DataQWords];

            public ref IntPtr this[int index]
            {
                get
                {
                    Helpers.DAssert(index < DataQWords * sizeof(ulong) / IntPtr.Size);
                    return ref Unsafe.Add(ref Unsafe.As<ulong, IntPtr>(ref data[0]), index);
                }
            }
        }

        protected override void InstallJitHook(IntPtr jit)
        {
            // Native jit hooks takes priority if they install successfully
            if (((System.Features & SystemFeature.MayUseNativeJitHooks) != 0) && InstallNativeJitHook(jit))
                return;
            
            InstallManagedJitHook(jit);
        }

        private Delegate? ourCompileMethodHookPost;

        protected virtual unsafe bool InstallNativeJitHook(IntPtr jit)
        {
            var hookConfig = GetNativeJitHookConfig();
            if (hookConfig == null)
                return false;

            CheckVersionGuid(jit);

            // Get the real compile method vtable slot
            var compileMethodSlot = GetVTableEntry(jit, VtableIndexICorJitCompilerCompileMethod);

            var compileHookPost = CastCompileMethodHookPostToRealType(CreateCompileMethodHookPostDelegate());
            ourCompileMethodHookPost = compileHookPost;

            var ourCompileMethodHookPostPtr = Marshal.GetFunctionPointerForDelegate(compileHookPost);

            V21.CORINFO_METHOD_INFO methodInfo;
            byte* nativeStart;
            uint nativeSize;
            AllocMemArgs args;
            V60.InvokeCompileMethodHookPostPtr.InvokeCompileMethodHookPost(ourCompileMethodHookPostPtr, IntPtr.Zero, IntPtr.Zero, &methodInfo, 0, &nativeStart, &nativeSize, CorJitResult.CORJIT_OK, &args);

            hookConfig->compileMethod = *compileMethodSlot;
            hookConfig->compileMethodHookPost = ourCompileMethodHookPostPtr;
            var ourCompileMethodHookPtr = hookConfig->compileMethodHook;

            // and now we can install our method pointer as a JIT hook
            Span<byte> ptrData = stackalloc byte[sizeof(IntPtr)];
            MemoryMarshal.Write(ptrData, ref ourCompileMethodHookPtr);

            System.PatchData(PatchTargetKind.ReadOnly, (IntPtr)compileMethodSlot, ptrData, default);

            // now trigger the compilation of something to ensure that any patches deferred to first invoke are performed
            CompileMethodPatchPrimer();

            return true;
        }

        // This seemingly useless method must exist in order to trigger an invocation of the compileMethod hook the first time so it can perform any deferred init work.
        // TODO: Remove me when the Post hook method's allocMem patching is moved to a Pre or Init hook.
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void CompileMethodPatchPrimer()
        {
        }

        protected unsafe override Delegate CreateCompileMethodDelegate(IntPtr compileMethod)
        {
            return new JitHookDelegateHolder(this, InvokeCompileMethodPtr, compileMethod).CompileMethodHook;
        }

        [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Only instantiated once, and has to not be disposed otherwise stuff will break.")]
        private sealed class JitHookDelegateHolder
        {
            public readonly Core60Runtime Runtime;
            public readonly INativeExceptionHelper? NativeExceptionHelper;
            public readonly GetExceptionSlot? GetNativeExceptionSlot;
            public readonly JitHookHelpersHolder JitHookHelpers;
            public readonly InvokeCompileMethodPtr InvokeCompileMethodPtr;
            public readonly IntPtr CompileMethodPtr;

            public readonly ThreadLocal<IAllocatedMemory> iCorJitInfoWrapper = new();
            public readonly ReadOnlyMemory<IAllocatedMemory> iCorJitInfoWrapperAllocs;
            public readonly IntPtr iCorJitInfoWrapperVtbl;

            public JitHookDelegateHolder(Core60Runtime runtime, InvokeCompileMethodPtr icmp, IntPtr compileMethod)
            {
                Runtime = runtime;
                NativeExceptionHelper = runtime.NativeExceptionHelper;
                JitHookHelpers = runtime.JitHookHelpers;
                InvokeCompileMethodPtr = icmp;
                CompileMethodPtr = compileMethod;

                iCorJitInfoWrapperVtbl = Marshal.AllocHGlobal(IntPtr.Size * runtime.ICorJitInfoFullVtableCount);
                iCorJitInfoWrapperAllocs = Runtime.arch.CreateNativeVtableProxyStubs(iCorJitInfoWrapperVtbl, runtime.ICorJitInfoFullVtableCount);
                unsafe { Runtime.PatchWrapperVtable((IntPtr*)iCorJitInfoWrapperVtbl); }
                MMDbgLog.Trace($"Allocated ICorJitInfo wrapper vtable at 0x{iCorJitInfoWrapperVtbl:x16}");

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
                byte** nativeEntry,
                uint* nativeSizeOfCode)
            {
                if (jit == IntPtr.Zero)
                    return CorJitResult.CORJIT_OK;

                *nativeEntry = null;
                *nativeSizeOfCode = 0;

                var lastError = MarshalEx.GetLastPInvokeError();
                nint nativeException = default;
                var pNEx = GetNativeExceptionSlot is { } getNex ? getNex() : null;
                hookEntrancy++;
                try
                {

                    if (hookEntrancy == 1)
                    {
                        try
                        {
                            var corJitWrapper = iCorJitInfoWrapper.Value;
                            if (corJitWrapper is null)
                            {
                                // we need to create corJitWrapper
                                var allocReq = new AllocationRequest(sizeof(ICorJitInfoWrapper))
                                {
                                    Alignment = IntPtr.Size,
                                    Executable = false
                                };
                                if (Runtime.System.MemoryAllocator.TryAllocate(allocReq, out var alloc))
                                {
                                    iCorJitInfoWrapper.Value = corJitWrapper = alloc;
                                }
                            }
                            // we still need to check if we were able to create it, because not creating it should not be a hard error
                            if (corJitWrapper is not null)
                            {
                                var wrapper = (ICorJitInfoWrapper*)corJitWrapper.BaseAddress;
                                wrapper->Vtbl = iCorJitInfoWrapperVtbl;
                                wrapper->Wrapped = (IntPtr**)corJitInfo;
                                (*wrapper)[ICorJitInfoWrapper.HotCodeRW] = IntPtr.Zero;
                                (*wrapper)[ICorJitInfoWrapper.ColdCodeRW] = IntPtr.Zero;
                                corJitInfo = (IntPtr)wrapper;
                            }
                        }
                        catch (Exception e)
                        {
                            try
                            {
                                MMDbgLog.Error($"Error while setting up the ICorJitInfo wrapper: {e}");
                            }
                            catch
                            {

                            }
                        }
                    }

                    var result = InvokeCompileMethodPtr.InvokeCompileMethod(CompileMethodPtr,
                        jit, corJitInfo, methodInfo, flags, nativeEntry, nativeSizeOfCode);
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
                            // we need to make sure that we set up the wrapper to continue
                            var corJitWrapper = iCorJitInfoWrapper.Value;
                            if (corJitWrapper is null)
                                return result;

                            ref var wrapper = ref *(ICorJitInfoWrapper*)corJitWrapper.BaseAddress;
                            var rwEntry = wrapper[ICorJitInfoWrapper.HotCodeRW];

                            Runtime.CompileMethodHookPostCommon(methodInfo, nativeEntry, nativeSizeOfCode, rwEntry);
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

        private unsafe void CompileMethodHookPostCommon(V21.CORINFO_METHOD_INFO* methodInfo, byte** nativeEntry, uint* nativeSizeOfCode, IntPtr rwEntry)
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

            OnMethodCompiledCore(declaringType, method, genericClassArgs, genericMethodArgs, (IntPtr)(*nativeEntry), rwEntry, *nativeSizeOfCode);
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct NativeJitHookConfig
        {
            public IntPtr compileMethod;
            public IntPtr compileMethodHook;
            public IntPtr compileMethodHookPost;
            public IntPtr allocMem;
            public IntPtr allocMemHook;
        }

        protected unsafe virtual NativeJitHookConfig* GetNativeJitHookConfig() => (NativeJitHookConfig*)System.GetNativeJitHookConfig(60);

        protected unsafe virtual Delegate CreateCompileMethodHookPostDelegate()
        {
            return new JitHookPostDelegateHolder(this).CompileMethodHookPost;
        }

        protected virtual Delegate CastCompileMethodHookPostToRealType(Delegate del)
            => del.CastDelegate<V60.CompileMethodHookPostDelegate>();

        [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Only instantiated once, and has to not be disposed otherwise stuff will break.")]
        private sealed class JitHookPostDelegateHolder
        {
            public readonly Core60Runtime Runtime;
            public readonly JitHookHelpersHolder JitHookHelpers;
            
            public static volatile bool patchedICorJitInfo;
            public static readonly object patchedICorJitInfoSyncRoot = new object();

            public JitHookPostDelegateHolder(Core60Runtime runtime)
            {
                Runtime = runtime;
                JitHookHelpers = runtime.JitHookHelpers;
            }

            [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                Justification = "We want to swallow exceptions here to prevent them from bubbling out of the JIT")]
            public unsafe CorJitResult CompileMethodHookPost(
                IntPtr jit, // ICorJitCompiler*
                IntPtr corJitInfo, // ICorJitInfo*
                V21.CORINFO_METHOD_INFO* methodInfo, // CORINFO_METHOD_INFO*
                uint flags,
                byte** nativeEntry,
                uint* nativeSizeOfCode,
                CorJitResult res,
                AllocMemArgs* pArgs)
            {
                if (jit == IntPtr.Zero)
                    return res;

                try
                {
                    // To avoid the performance implications of having both a Pre and Post hook method, we defer the allocMem patching until the first post hook invocation.
                    // In order to prime this path, we trigger the compilation of a throw away dynamic method in InstallNativeJitHook.
                    // TODO: Consider moving this to a Pre hook that is either only called once or, ignoring performance, called every time and remove the dynamic method primer.
                    if (!patchedICorJitInfo)
                    {
                        lock (patchedICorJitInfoSyncRoot)
                        {
                            if (!patchedICorJitInfo)
                            {
                                var allocMemSlot = GetVTableEntry(corJitInfo, Runtime.VtableIndexICorJitInfoAllocMem);
                                var hookConfig = Runtime.GetNativeJitHookConfig();
                                hookConfig->allocMem = *allocMemSlot;

                                var ourAllocMemPtr = hookConfig->allocMemHook;
                                Span<byte> ptrData = stackalloc byte[sizeof(IntPtr)];
                                MemoryMarshal.Write(ptrData, ref ourAllocMemPtr);

                                Runtime.System.PatchData(PatchTargetKind.ReadOnly, (IntPtr)allocMemSlot, ptrData, default);
                                patchedICorJitInfo = true;
                            }
                        }
                    }

                    Runtime.CompileMethodHookPostCommon(methodInfo, nativeEntry, nativeSizeOfCode, pArgs->hotCodeBlockRW);
                }
                catch
                {
                    // eat the exception so we don't accidentally bubble up to native code
                }

                return res;
            }
        }

        private Delegate? allocMemDelegate;
        private IDisposable? n2mAllocMemHelper;

        protected unsafe virtual void PatchWrapperVtable(IntPtr* vtbl)
        {
            allocMemDelegate = CastAllocMemToRealType(CreateAllocMemDelegate());
            var allocMemFnPtr = EHNativeToManaged(Marshal.GetFunctionPointerForDelegate(allocMemDelegate), out n2mAllocMemHelper);

            // invoke our allocMemFnPtr through IAMP to ensure that the JIT has compiled any needed thunks
            InvokeAllocMemPtr.InvokeAllocMem(allocMemFnPtr, IntPtr.Zero, null);
            vtbl[VtableIndexICorJitInfoAllocMem] = allocMemFnPtr;
        }

        protected virtual int VtableIndexICorJitInfoAllocMem => V60.ICorJitInfoVtable.AllocMemIndex;
        protected virtual int ICorJitInfoFullVtableCount => V60.ICorJitInfoVtable.TotalVtableCount;

        protected virtual InvokeAllocMemPtr InvokeAllocMemPtr => V60.InvokeAllocMemPtr;

        protected override InvokeCompileMethodPtr InvokeCompileMethodPtr => V60.InvokeCompileMethodPtr;

        protected override Delegate CastCompileHookToRealType(Delegate del)
            => del.CastDelegate<V60.CompileMethodDelegate>();

        protected virtual Delegate CastAllocMemToRealType(Delegate del)
            => del.CastDelegate<V60.AllocMemDelegate>();

        protected unsafe virtual Delegate CreateAllocMemDelegate()
        {
            return new AllocMemDelegateHolder(this, InvokeAllocMemPtr).AllocMemHook;
        }

        private sealed class AllocMemDelegateHolder
        {
            public readonly Core60Runtime Runtime;
            public readonly INativeExceptionHelper? NativeExceptionHelper;
            public readonly GetExceptionSlot? GetNativeExceptionSlot;
            public readonly InvokeAllocMemPtr InvokeAllocMemPtr;
            public readonly int ICorJitInfoAllocMemIdx;
            public readonly ConcurrentDictionary<IntPtr, (IntPtr M2N, IDisposable?)> AllocMemExceptionHelperCache = new();

            public AllocMemDelegateHolder(Core60Runtime runtime, InvokeAllocMemPtr iamp)
            {
                Runtime = runtime;
                NativeExceptionHelper = runtime.NativeExceptionHelper;
                GetNativeExceptionSlot = NativeExceptionHelper?.GetExceptionSlot;
                InvokeAllocMemPtr = iamp;
                ICorJitInfoAllocMemIdx = Runtime.VtableIndexICorJitInfoAllocMem;

                // eagerly call iamp to make sure it's compiled
                unsafe { iamp.InvokeAllocMem(IntPtr.Zero, IntPtr.Zero, null); }
            }

            private IntPtr GetRealInvokePtr(IntPtr ptr)
            {
                if (NativeExceptionHelper is null)
                    return ptr;
                return AllocMemExceptionHelperCache.GetOrAdd(ptr, p => (Runtime.EHManagedToNative(p, out var h), h)).M2N;
            }

            public unsafe void AllocMemHook(IntPtr thisPtr, V60.AllocMemArgs* args)
            {
                if (thisPtr == IntPtr.Zero)
                    return;

                var wrap = (ICorJitInfoWrapper*)thisPtr;
                var wrapped = wrap->Wrapped;
                InvokeAllocMemPtr.InvokeAllocMem(GetRealInvokePtr((*wrapped)[ICorJitInfoAllocMemIdx]), (IntPtr)wrapped, args);
                if (GetNativeExceptionSlot is { } neh && (nint)(*neh()) is not 0)
                {
                    return;
                }
                (*wrap)[ICorJitInfoWrapper.HotCodeRW] = args->hotCodeBlockRW;
                (*wrap)[ICorJitInfoWrapper.ColdCodeRW] = args->coldCodeBlockRW;
            }
        }

#if NET6_USE_RUNTIME_INTROSPECTION
        public override RuntimeFeature Features 
            => base.Features & ~RuntimeFeature.RequiresBodyThunkWalking;

        private unsafe IntPtr GetMethodBodyPtr(MethodBase method, RuntimeMethodHandle handle) {
            var md = (V60.MethodDesc*) handle.Value;

            md = V60.MethodDesc.FindTightlyBoundWrappedMethodDesc(md);

            return (IntPtr) md->GetNativeCode();
        }

        public override unsafe IntPtr GetMethodEntryPoint(MethodBase method) {
            method = GetIdentifiable(method);
            var handle = GetMethodHandle(method);

            GetPtr:
            var ptr = GetMethodBodyPtr(method, handle);
            if (ptr == IntPtr.Zero) { // the method hasn't been JITted yet
                // TODO: call PlatformTriple.Prepare instead to handle generic methods
                RuntimeHelpers.PrepareMethod(handle);
                goto GetPtr;
            }

            return ptr;
        }
#endif
    }
}
