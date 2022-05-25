using MonoMod.Core.Utils;
using System;
using System.Reflection;

namespace MonoMod.Core.Platforms {
    public interface IRuntime {
        RuntimeKind Target { get; }

        RuntimeFeature Features { get; }

        Abi Abi { get; }

        event OnMethodCompiledCallback? OnMethodCompiled;

        MethodBase GetIdentifiable(MethodBase method);
        RuntimeMethodHandle GetMethodHandle(MethodBase method);

        void DisableInlining(MethodBase method);

        IDisposable? PinMethodIfNeeded(MethodBase method);

        IntPtr GetMethodEntryPoint(MethodBase method);
    }

    public delegate void OnMethodCompiledCallback(MethodBase? method, IntPtr codeStart, ulong codeSize);
}
