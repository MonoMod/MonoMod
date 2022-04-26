#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
#define HAS_STRING_COMPARISON
#endif

using System.Runtime.CompilerServices;

namespace System {
    public static class StringExtensions {

        // TODO: properly polyfill StringComparison checks for old runtimes
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Replace(this string self, string oldValue, string newValue, StringComparison comparison) {
            if (self is null)
                ThrowHelper.ThrowArgumentNullException(nameof(self));
#if HAS_STRING_COMPARISON
            return self.Replace(oldValue, newValue, comparison);
#else
            return self.Replace(oldValue, newValue);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string self, string value, StringComparison comparison) {
            if (self is null)
                ThrowHelper.ThrowArgumentNullException(nameof(self));
#if HAS_STRING_COMPARISON
            return self.Contains(value, comparison);
#else
            return self.Contains(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashCode(this string self, StringComparison comparison) {
            if (self is null)
                ThrowHelper.ThrowArgumentNullException(nameof(self));
#if HAS_STRING_COMPARISON
            return self.GetHashCode(comparison);
#else
            return self.GetHashCode();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(this string self, char value, StringComparison comparison) {
            if (self is null)
                ThrowHelper.ThrowArgumentNullException(nameof(self));
#if HAS_STRING_COMPARISON
            return self.IndexOf(value, comparison);
#else
            return self.IndexOf(value);
#endif
        }
    }
}
