#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
#define HAS_FROM_COMPARISON
#endif

#if HAS_FROM_COMPARISON
using System.Runtime.CompilerServices;
#endif

namespace System
{
    /// <summary>
    /// Extensions to <see cref="StringComparer"/> providing consistent access to APIs introduced after the type.
    /// </summary>
    public static class StringComparerEx
    {
#if HAS_FROM_COMPARISON
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static StringComparer FromComparison(StringComparison comparisonType)
        {
#if HAS_FROM_COMPARISON
            return StringComparer.FromComparison(comparisonType);
#else
            return comparisonType switch {
                StringComparison.CurrentCulture => StringComparer.CurrentCulture,
                StringComparison.CurrentCultureIgnoreCase => StringComparer.CurrentCultureIgnoreCase,
                StringComparison.InvariantCulture => StringComparer.InvariantCulture,
                StringComparison.InvariantCultureIgnoreCase => StringComparer.InvariantCultureIgnoreCase,
                StringComparison.Ordinal => StringComparer.Ordinal,
                StringComparison.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
                _ => throw new ArgumentException("Invalid StringComparison value", nameof(comparisonType)),
            };
#endif
        }
    }
}
