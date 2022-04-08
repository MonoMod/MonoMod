namespace System {
    public static class ArrayEx {
        private static class TypeHolder<T> {
            public static readonly T[] Empty = new T[0];
        }

        public static T[] Empty<T>() => TypeHolder<T>.Empty;
    }
}
