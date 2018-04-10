using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public interface IDetour {
        bool IsValid { get; }

        void Apply();
        void Undo();
        void Free();

        MethodBase GenerateTrampoline(MethodBase signature = null);
        T GenerateTrampoline<T>() where T : class;
    }
}
