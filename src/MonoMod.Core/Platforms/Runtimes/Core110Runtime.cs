using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using static MonoMod.Core.Interop.CoreCLR;

namespace MonoMod.Core.Platforms.Runtimes
{
    [SuppressMessage("Performance", "CA1852", Justification = "This type will be derived for .NET 12.")]
    internal class Core110Runtime : Core100Runtime
    {
        public Core110Runtime(ISystem system, IArchitecture arch) : base(system, arch) { }

        // src/coreclr/inc/jiteeversionguid.h
        // 65743063-e8fa-41d4-9496-c436974c00f5
        private static readonly Guid JitVersionGuid = new(
            0x65743063,
            0xe8fa,
            0x41d4,
            0x94, 0x96, 0xc4, 0x36, 0x97, 0x4c, 0x00, 0xf5
        );

        protected override Guid ExpectedJitVersion => JitVersionGuid;

        protected override int VtableIndexICorJitInfoAllocMem => V110.ICorJitInfoVtable.AllocMemIndex;
        protected override int ICorJitInfoFullVtableCount => V110.ICorJitInfoVtable.TotalVtableCount;

        protected override Delegate CastAllocMemToRealType(Delegate del)
            => del.CastDelegate<V110.AllocMemDelegate>();

        protected override unsafe Delegate CreateAllocMemDelegate()
        {
            return new AllocMemDelegateHolder(this, V110.InvokeAllocMemFn).AllocMemHook;
        }

        private sealed class AllocMemDelegateHolder
        {
            public readonly Core110Runtime Runtime;
            public readonly INativeExceptionHelper? NativeExceptionHelper;
            public readonly GetExceptionSlot? GetNativeExceptionSlot;
            public readonly V110.InvokeAllocMemPtr InvokeAllocMemPtr;
            public readonly int ICorJitInfoAllocMemIdx;
            public readonly ConcurrentDictionary<IntPtr, (IntPtr M2N, IDisposable?)> AllocMemExceptionHelperCache = new();

            public AllocMemDelegateHolder(Core110Runtime runtime, V110.InvokeAllocMemPtr iamp)
            {
                Runtime = runtime;
                NativeExceptionHelper = runtime.NativeExceptionHelper;
                GetNativeExceptionSlot = NativeExceptionHelper?.GetExceptionSlot;
                InvokeAllocMemPtr = iamp;
                ICorJitInfoAllocMemIdx = Runtime.VtableIndexICorJitInfoAllocMem;

                unsafe { iamp.InvokeAllocMem(IntPtr.Zero, IntPtr.Zero, null); }
            }

            private IntPtr GetRealInvokePtr(IntPtr ptr)
            {
                if (NativeExceptionHelper is null)
                    return ptr;
                return AllocMemExceptionHelperCache.GetOrAdd(ptr, p => (Runtime.EHManagedToNative(p, out var h), h)).M2N;
            }

            public unsafe void AllocMemHook(IntPtr thisPtr, V110.AllocMemArgs* args)
            {
                if (thisPtr == IntPtr.Zero)
                    return;

                var wrap = (ICorJitInfoWrapper*)thisPtr;
                var wrapped = wrap->Wrapped;
                InvokeAllocMemPtr.InvokeAllocMem(GetRealInvokePtr((*wrapped)[ICorJitInfoAllocMemIdx]), (IntPtr)wrapped, args);
                if (GetNativeExceptionSlot is { } neh && (nint)(*neh()) is not 0)
                    return;

                (*wrap)[ICorJitInfoWrapper.HotCodeRW] = IntPtr.Zero;
                (*wrap)[ICorJitInfoWrapper.ColdCodeRW] = IntPtr.Zero;
                if (args is null || args->chunks is null)
                    return;

                for (uint i = 0; i < args->chunksCount; i++)
                {
                    ref var chunk = ref args->chunks[i];
                    if ((chunk.flags & V110.CorJitAllocMemFlag.HotCode) != 0)
                        (*wrap)[ICorJitInfoWrapper.HotCodeRW] = chunk.blockRW;
                    else if ((chunk.flags & V110.CorJitAllocMemFlag.ColdCode) != 0)
                        (*wrap)[ICorJitInfoWrapper.ColdCodeRW] = chunk.blockRW;
                }
            }
        }
    }
}
