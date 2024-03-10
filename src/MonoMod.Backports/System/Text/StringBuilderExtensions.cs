#if NET40_OR_GREATER || NETCOREAPP || NETSTANDARD
#define HAS_CLEAR
#endif

#if HAS_CLEAR
using System.Runtime.CompilerServices;
#endif

namespace System.Text
{
    public static class StringBuilderExtensions
    {
#if HAS_CLEAR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static StringBuilder Clear(this StringBuilder builder)
        {
            ThrowHelper.ThrowIfArgumentNull(builder, nameof(builder));
#if HAS_CLEAR
            return builder.Clear();
#else
            builder.Length = 0;
            return builder;
#endif
        }
    }
}
