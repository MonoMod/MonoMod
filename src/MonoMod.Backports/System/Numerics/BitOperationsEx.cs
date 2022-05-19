#if NET6_0_OR_GREATER
#define HAS_ISPOW2
#define HAS_ROUNDPOW2
#endif

#if NET7_0_OR_GREATER
#define HAS_NINT_VARIANTS
#endif

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Numerics {
    /// <summary>
    /// Extensions to <see cref="StringComparer"/> providing consistent access to APIs introduced after the type.
    /// </summary>
    public static class BitOperationsEx {
        #region IsPow2
        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(int value)
#if HAS_ISPOW2
            => BitOperations.IsPow2(value);
#else
            => (value & (value - 1)) == 0 && value > 0;
#endif

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static bool IsPow2(uint value)
#if HAS_ISPOW2
            => BitOperations.IsPow2(value);
#else
            => (value & (value - 1)) == 0 && value > 0;
#endif

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(long value)
#if HAS_ISPOW2
            => BitOperations.IsPow2(value);
#else
            => (value & (value - 1)) == 0 && value > 0;
#endif

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static bool IsPow2(ulong value)
#if HAS_ISPOW2
            => BitOperations.IsPow2(value);
#else
            => (value & (value - 1)) == 0 && value > 0;
#endif

        // the nint and nuint version don't exist in the BCL, only here
        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(nint value)
#if HAS_ISPOW2 && HAS_NINT_VARIANTS
            => BitOperations.IsPow2(value);
#else
            => (value & (value - 1)) == 0 && value > 0;
#endif

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static bool IsPow2(nuint value)
#if HAS_ISPOW2 && HAS_NINT_VARIANTS
            => BitOperations.IsPow2(value);
#else
            => (value & (value - 1)) == 0 && value > 0;
#endif
        #endregion

        #region RoundUpToPowerOf2
        /// <summary>Round the given integral value up to a power of 2.</summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The smallest power of 2 which is greater than or equal to <paramref name="value"/>.
        /// If <paramref name="value"/> is 0 or the result overflows, returns 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static uint RoundUpToPowerOf2(uint value)
#if HAS_ROUNDPOW2
            => BitOperations.RoundUpToPowerOf2(value);
#else
        {
            // Based on https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
#endif

        /// <summary>
        /// Round the given integral value up to a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The smallest power of 2 which is greater than or equal to <paramref name="value"/>.
        /// If <paramref name="value"/> is 0 or the result overflows, returns 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong RoundUpToPowerOf2(ulong value)
#if HAS_ROUNDPOW2
            => BitOperations.RoundUpToPowerOf2(value);
#else
        {
            // Based on https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1;
        }
#endif

        /// <summary>
        /// Round the given integral value up to a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The smallest power of 2 which is greater than or equal to <paramref name="value"/>.
        /// If <paramref name="value"/> is 0 or the result overflows, returns 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static nuint RoundUpToPowerOf2(nuint value)
#if HAS_ROUNDPOW2 && HAS_NINT_VARIANTS
            => BitOperations.RoundUpToPowerOf2(value);
#else
        {
            if (IntPtr.Size == 8) {
                return (nuint) RoundUpToPowerOf2((ulong) value);
            } else {
                return (nuint) RoundUpToPowerOf2((uint) value);
            }
        }
#endif
        #endregion

        #region LeadingZeroCount
        /// <summary>
        /// Count the number of leading zero bits in a mask.
        /// Similar in behavior to the x86 instruction LZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int LeadingZeroCount(uint value) => BitOperations.LeadingZeroCount(value);

        /// <summary>
        /// Count the number of leading zero bits in a mask.
        /// Similar in behavior to the x86 instruction LZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int LeadingZeroCount(ulong value) => BitOperations.LeadingZeroCount(value);

        /// <summary>
        /// Count the number of leading zero bits in a mask.
        /// Similar in behavior to the x86 instruction LZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int LeadingZeroCount(nuint value)
#if HAS_NINT_VARIANTS
            => BitOperations.LeadingZeroCount(value);
#else
        {
            if (IntPtr.Size == 8) {
                return LeadingZeroCount((ulong) value);
            } else {
                return LeadingZeroCount((uint) value);
            }
        }
#endif
        #endregion

        #region Log2
        /// <summary>
        /// Returns the integer (floor) log of the specified value, base 2.
        /// Note that by convention, input value 0 returns 0 since log(0) is undefined.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int Log2(uint value) => BitOperations.Log2(value);

        /// <summary>
        /// Returns the integer (floor) log of the specified value, base 2.
        /// Note that by convention, input value 0 returns 0 since log(0) is undefined.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int Log2(ulong value) => BitOperations.Log2(value);

        /// <summary>
        /// Returns the integer (floor) log of the specified value, base 2.
        /// Note that by convention, input value 0 returns 0 since log(0) is undefined.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int Log2(nuint value) 
#if HAS_NINT_VARIANTS
            => BitOperations.LeadingZeroCount(value);
#else
        {
            if (IntPtr.Size == 8) {
                return Log2((ulong) value);
            } else {
                return Log2((uint) value);
            }
        }
#endif
        #endregion

        #region PopCount
        /// <summary>
        /// Returns the population count (number of bits set) of a mask.
        /// Similar in behavior to the x86 instruction POPCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int PopCount(uint value) => BitOperations.PopCount(value);

        /// <summary>
        /// Returns the population count (number of bits set) of a mask.
        /// Similar in behavior to the x86 instruction POPCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int PopCount(ulong value) => BitOperations.PopCount(value);

        /// <summary>
        /// Returns the population count (number of bits set) of a mask.
        /// Similar in behavior to the x86 instruction POPCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int PopCount(nuint value)
#if HAS_NINT_VARIANTS
            => BitOperations.PopCount(value);
#else
        {
            if (IntPtr.Size == 8) {
                return PopCount((ulong) value);
            } else {
                return PopCount((uint) value);
            }
        }
#endif
        #endregion

        #region TrailingZeroCount
        /// <summary>
        /// Count the number of trailing zero bits in an integer value.
        /// Similar in behavior to the x86 instruction TZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(int value) => BitOperations.TrailingZeroCount(value);

        /// <summary>
        /// Count the number of trailing zero bits in an integer value.
        /// Similar in behavior to the x86 instruction TZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(uint value) => BitOperations.TrailingZeroCount(value);

        /// <summary>
        /// Count the number of trailing zero bits in a mask.
        /// Similar in behavior to the x86 instruction TZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(long value) => BitOperations.TrailingZeroCount(value);

        /// <summary>
        /// Count the number of trailing zero bits in a mask.
        /// Similar in behavior to the x86 instruction TZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int TrailingZeroCount(ulong value) => BitOperations.TrailingZeroCount(value);

        /// <summary>
        /// Count the number of trailing zero bits in a mask.
        /// Similar in behavior to the x86 instruction TZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(nint value)
#if HAS_NINT_VARIANTS
            => BitOperations.TrailingZeroCount(value);
#else
        {
            if (IntPtr.Size == 8) {
                return TrailingZeroCount((long) value);
            } else {
                return TrailingZeroCount((int) value);
            }
        }
#endif

        /// <summary>
        /// Count the number of trailing zero bits in a mask.
        /// Similar in behavior to the x86 instruction TZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static int TrailingZeroCount(nuint value)
#if HAS_NINT_VARIANTS
            => BitOperations.TrailingZeroCount(value);
#else
        {
            if (IntPtr.Size == 8) {
                return TrailingZeroCount((ulong) value);
            } else {
                return TrailingZeroCount((uint) value);
            }
        }
#endif
        #endregion

        #region RotateLeft
        /// <summary>
        /// Rotates the specified value left by the specified number of bits.
        /// Similar in behavior to the x86 instruction ROL.
        /// </summary>
        /// <param name="value">The value to rotate.</param>
        /// <param name="offset">The number of bits to rotate by.
        /// Any value outside the range [0..31] is treated as congruent mod 32.</param>
        /// <returns>The rotated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static uint RotateLeft(uint value, int offset) => BitOperations.RotateLeft(value, offset);

        /// <summary>
        /// Rotates the specified value left by the specified number of bits.
        /// Similar in behavior to the x86 instruction ROL.
        /// </summary>
        /// <param name="value">The value to rotate.</param>
        /// <param name="offset">The number of bits to rotate by.
        /// Any value outside the range [0..63] is treated as congruent mod 64.</param>
        /// <returns>The rotated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong RotateLeft(ulong value, int offset) => BitOperations.RotateLeft(value, offset);

        /// <summary>
        /// Rotates the specified value left by the specified number of bits.
        /// Similar in behavior to the x86 instruction ROL.
        /// </summary>
        /// <param name="value">The value to rotate.</param>
        /// <param name="offset">The number of bits to rotate by.
        /// Any value outside the range [0..31] is treated as congruent mod 32 on a 32-bit process,
        /// and any value outside the range [0..63] is treated as congruent mod 64 on a 64-bit process.</param>
        /// <returns>The rotated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static nuint RotateLeft(nuint value, int offset)
#if HAS_NINT_VARIANTS
            => BitOperations.RotateLeft(value, offset);
#else
        {
            if (IntPtr.Size == 8) {
                return (nuint) RotateLeft((ulong) value, offset);
            } else {
                return (nuint) RotateLeft((uint) value, offset);
            }
        }
#endif
        #endregion

        #region RotateRight
        /// <summary>
        /// Rotates the specified value right by the specified number of bits.
        /// Similar in behavior to the x86 instruction ROR.
        /// </summary>
        /// <param name="value">The value to rotate.</param>
        /// <param name="offset">The number of bits to rotate by.
        /// Any value outside the range [0..31] is treated as congruent mod 32.</param>
        /// <returns>The rotated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static uint RotateRight(uint value, int offset) => BitOperations.RotateRight(value, offset);

        /// <summary>
        /// Rotates the specified value right by the specified number of bits.
        /// Similar in behavior to the x86 instruction ROR.
        /// </summary>
        /// <param name="value">The value to rotate.</param>
        /// <param name="offset">The number of bits to rotate by.
        /// Any value outside the range [0..63] is treated as congruent mod 64.</param>
        /// <returns>The rotated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong RotateRight(ulong value, int offset) => BitOperations.RotateRight(value, offset);

        /// <summary>
        /// Rotates the specified value right by the specified number of bits.
        /// Similar in behavior to the x86 instruction ROR.
        /// </summary>
        /// <param name="value">The value to rotate.</param>
        /// <param name="offset">The number of bits to rotate by.
        /// Any value outside the range [0..31] is treated as congruent mod 32 on a 32-bit process,
        /// and any value outside the range [0..63] is treated as congruent mod 64 on a 64-bit process.</param>
        /// <returns>The rotated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static nuint RotateRight(nuint value, int offset)
#if HAS_NINT_VARIANTS
            => BitOperations.RotateLeft(value, offset);
#else
        {
            if (IntPtr.Size == 8) {
                return (nuint) RotateRight((ulong) value, offset);
            } else {
                return (nuint) RotateRight((uint) value, offset);
            }
        }
#endif
        #endregion
    }
}
