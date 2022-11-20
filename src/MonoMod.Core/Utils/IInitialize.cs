namespace MonoMod.Core.Utils {
    public interface IInitialize {
        void Initialize();
    }

    public interface IInitialize<T> {
        void Initialize(T value);
    }
}
