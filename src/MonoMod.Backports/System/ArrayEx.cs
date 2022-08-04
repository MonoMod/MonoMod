#if NETCOREAPP1_0_OR_GREATER || NET46_OR_GREATER || NETSTANDARD1_3_OR_GREATER
#define HAS_EMPTY
#endif

#if NET6_0_OR_GREATER
#define HAS_MAXLENGTH
#endif

using System.Runtime.CompilerServices;

namespace System {
    /// <summary>
    /// Extensions to <see cref="Array"/> providing consistent access to APIs introduced after the type.
    /// </summary>
    public static class ArrayEx {
#if !HAS_EMPTY
        private static class TypeHolder<T> {
            public static readonly T[] Empty = new T[0];
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] Empty<T>()
#if HAS_EMPTY
            => Array.Empty<T>();
#else
            => TypeHolder<T>.Empty;
#endif

        public static int MaxLength
#if HAS_MAXLENGTH
            => Array.MaxLength;
#else
            => 0x6FFFFFFF; // this is a total estimate, intentionally kept smaller than the value in the .NET Core BCL
#endif
    }
}
