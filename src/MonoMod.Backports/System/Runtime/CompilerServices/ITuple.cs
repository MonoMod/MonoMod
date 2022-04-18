
namespace System.Runtime.CompilerServices {

    // TODO: possibly make this public, even if it means that for some targets the BCL tuples don't implement it

    /// <summary>
    /// This interface is required for types that want to be indexed into by dynamic patterns.
    /// </summary>
    internal interface ITuple {
        /// <summary>
        /// The number of positions in this data structure.
        /// </summary>
        int Length { get; }

        /// <summary>
        /// Get the element at position <param name="index"/>.
        /// </summary>
        object? this[int index] { get; }
    }
}
