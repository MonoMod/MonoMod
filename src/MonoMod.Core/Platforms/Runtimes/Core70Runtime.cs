using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    internal class Core70Runtime : Core60Runtime
    {

        private readonly IArchitecture arch;

        public Core70Runtime(ISystem system, IArchitecture arch) : base(system)
        {
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
                    Helpers.DAssert(index < (DataQWords * sizeof(ulong)) / IntPtr.Size);
                    return ref Unsafe.Add(ref Unsafe.As<ulong, IntPtr>(ref data[0]), index);
                }
            }
        }

        protected unsafe override Delegate CreateCompileMethodDelegate(IntPtr compileMethod)
        {
            return new JitHookDelegateHolder(this, InvokeCompileMethodPtr, compileMethod).CompileMethodHook;
        }

        [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Only instantiated once, and has to not be disposed otherwise stuff will break.")]
        private sealed class JitHookDelegateHolder
        {
            public readonly Core70Runtime Runtime;
            public readonly INativeExceptionHelper? NativeExceptionHelper;
            public readonly GetExceptionSlot? GetNativeExceptionSlot;
            public readonly JitHookHelpersHolder JitHookHelpers;
            public readonly InvokeCompileMethodPtr InvokeCompileMethodPtr;
            public readonly IntPtr CompileMethodPtr;

            public readonly ThreadLocal<IAllocatedMemory> iCorJitInfoWrapper = new();
            public readonly ReadOnlyMemory<IAllocatedMemory> iCorJitInfoWrapperAllocs;
            public readonly IntPtr iCorJitInfoWrapperVtbl;

            public JitHookDelegateHolder(Core70Runtime runtime, InvokeCompileMethodPtr icmp, IntPtr compileMethod)
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

                *nativeEntry = null;
                *nativeSizeOfCode = 0;

                if (jit == IntPtr.Zero)
                    return CorJitResult.CORJIT_OK;

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

                            Runtime.OnMethodCompiledCore(declaringType, method, genericClassArgs, genericMethodArgs, (IntPtr)(*nativeEntry), rwEntry, *nativeSizeOfCode);
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

        private Delegate? allocMemDelegate;
        private IDisposable? n2mAllocMemHelper;

        protected unsafe virtual void PatchWrapperVtable(IntPtr* vtbl)
        {
            allocMemDelegate = CastAllocMemToRealType(CreateAllocMemDelegate());
            vtbl[VtableIndexICorJitInfoAllocMem] = EHNativeToManaged(Marshal.GetFunctionPointerForDelegate(allocMemDelegate), out n2mAllocMemHelper);
        }

        protected virtual int VtableIndexICorJitInfoAllocMem => V70.ICorJitInfoVtable.AllocMemIndex;
        protected virtual int ICorJitInfoFullVtableCount => V70.ICorJitInfoVtable.TotalVtableCount;

        protected virtual InvokeAllocMemPtr InvokeAllocMemPtr => V70.InvokeAllocMemPtr;

        protected virtual Delegate CastAllocMemToRealType(Delegate del)
            => del.CastDelegate<V70.AllocMemDelegate>();

        protected unsafe virtual Delegate CreateAllocMemDelegate()
        {
            return new AllocMemDelegateHolder(this, InvokeAllocMemPtr).AllocMemHook;
        }

        private sealed class AllocMemDelegateHolder
        {
            public readonly Core70Runtime Runtime;
            public readonly INativeExceptionHelper? NativeExceptionHelper;
            public readonly GetExceptionSlot? GetNativeExceptionSlot;
            public readonly InvokeAllocMemPtr InvokeAllocMemPtr;
            public readonly int ICorJitInfoAllocMemIdx;
            public readonly ConcurrentDictionary<IntPtr, (IntPtr M2N, IDisposable?)> AllocMemExceptionHelperCache = new();

            public AllocMemDelegateHolder(Core70Runtime runtime, InvokeAllocMemPtr iamp)
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

            public unsafe void AllocMemHook(IntPtr thisPtr, V70.AllocMemArgs* args)
            {
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
    }
}
