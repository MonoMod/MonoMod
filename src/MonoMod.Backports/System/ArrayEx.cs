namespace System {
    public static class ArrayEx {
        private static class TypeHolder<T> {
            public static readonly T[] Empty = new T[0];
        }

        public static T[] Empty<T>() => TypeHolder<T>.Empty;

        public static int MaxLength
#if NET6_0_OR_GREATER
            => Array.MaxLength;
#else
            => 0x6FFFFFFF; // this is a total estimate, intentionally kept smaller than the value in the .NET Core BCL
#endif
    }
}
