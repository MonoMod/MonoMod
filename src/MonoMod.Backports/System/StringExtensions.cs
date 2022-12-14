#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
#define HAS_STRING_COMPARISON
#endif

using System.Runtime.CompilerServices;

namespace System {
    public static class StringExtensions {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Replace(this string self, string oldValue, string newValue, StringComparison comparison) {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            ThrowHelper.ThrowIfArgumentNull(oldValue, ExceptionArgument.oldValue);
            ThrowHelper.ThrowIfArgumentNull(newValue, ExceptionArgument.newValue);

#if HAS_STRING_COMPARISON
            return self.Replace(oldValue, newValue, comparison);
#else
            // we're gonna do a bit of tomfoolery
            var ish = new DefaultInterpolatedStringHandler(oldValue.Length, 0);
            var from = self.AsSpan();
            var old = oldValue.AsSpan();
            while (true) {
                var idx = from.IndexOf(old, comparison);
                if (idx < 0) {
                    ish.AppendFormatted(from);
                    break;
                }
                ish.AppendFormatted(from.Slice(0, idx));
                ish.AppendLiteral(newValue);
                from = from.Slice(idx + old.Length);
            }
            return ish.ToStringAndClear();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string self, string value, StringComparison comparison) {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
            ThrowHelper.ThrowIfArgumentNull(value, ExceptionArgument.value);
#if HAS_STRING_COMPARISON
            return self.Contains(value, comparison);
#else
            return self.IndexOf(value, comparison) >= 0;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string self, char value, StringComparison comparison) {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
#if HAS_STRING_COMPARISON
            return self.Contains(value, comparison);
#else
            return self.IndexOf(value, comparison) >= 0;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashCode(this string self, StringComparison comparison) {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
#if HAS_STRING_COMPARISON
            return self.GetHashCode(comparison);
#else

            return StringComparerEx.FromComparison(comparison).GetHashCode(self);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(this string self, char value, StringComparison comparison) {
            ThrowHelper.ThrowIfArgumentNull(self, ExceptionArgument.self);
#if HAS_STRING_COMPARISON
            return self.IndexOf(value, comparison);
#else
            return self.IndexOf(new string(value, 1), comparison);
#endif
        }
    }
}
