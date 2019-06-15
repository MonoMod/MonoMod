using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public interface IDetour : IDisposable {
        bool IsValid { get; }
        bool IsApplied { get; }

        void Apply();
        void Undo();
        void Free();

        MethodBase GenerateTrampoline(MethodBase signature = null);
        T GenerateTrampoline<T>() where T : Delegate;
    }
}
