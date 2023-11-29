using System.Collections.Generic;

namespace System.Collections.Concurrent {
    /// <summary>
    /// Out-of-the-box partitioners are created with a set of default behaviors.
    /// For example, by default, some form of buffering and chunking will be employed to achieve
    /// optimal performance in the common scenario where an <see cref="IEnumerable{T}"/> implementation is fast and
    /// non-blocking.  These behaviors can be overridden via this enumeration.
    /// </summary>
    [Flags]
    public enum EnumerablePartitionerOptions {
        /// <summary>
        /// Use the default behavior (i.e., use buffering to achieve optimal performance)
        /// </summary>
        None = 0x0,

        /// <summary>
        /// Creates a partitioner that will take items from the source enumerable one at a time
        /// and will not use intermediate storage that can be accessed more efficiently by multiple threads.
        /// This option provides support for low latency (items will be processed as soon as they are available from
        /// the source) and partial support for dependencies between items (a thread cannot deadlock waiting for an item
        /// that it, itself, is responsible for processing).
        /// </summary>
        NoBuffering = 0x1
    }
}
