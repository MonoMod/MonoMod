#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
#define HAS_ISBYREFLIKE
#endif

namespace System {
    public static class TypeExtensions {
        public static bool IsByRefLike(this Type type) {
            ThrowHelper.ThrowIfArgumentNull(type, ExceptionArgument.type);
            if (type is null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.type);

#if HAS_ISBYREFLIKE
            return type.IsByRefLike;
#else
            // TODO: cache this information somehow
            foreach (var attr in type.GetCustomAttributes(false)) {
                if (attr.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                    return true;
            }

            return false;
#endif
        }
    }
}
