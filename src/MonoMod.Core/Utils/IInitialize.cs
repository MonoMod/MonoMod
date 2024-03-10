namespace MonoMod.Core.Utils
{
    /// <summary>
    /// An object which must be initialized after construction.
    /// </summary>
    public interface IInitialize
    {
        /// <summary>
        /// Initializes this instance.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// An object which must be initialized with some value after construction.
    /// </summary>
    /// <typeparam name="T">The type of value this must be initialized with.</typeparam>
    public interface IInitialize<T>
    {
        /// <summary>
        /// Initializes this instance.
        /// </summary>
        /// <param name="value">The requested value.</param>
        void Initialize(T value);
    }
}
