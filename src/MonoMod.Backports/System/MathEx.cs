#if NET6_0_OR_GREATER
#define HAS_CLAMP
#endif

using System.Runtime.CompilerServices;
#if !HAS_CLAMP
using System.Diagnostics.CodeAnalysis;
#endif

namespace System
{
    /// <summary>
    /// Extensions to <see cref="StringComparer"/> providing consistent access to APIs introduced after the type.
    /// </summary>
    public static class MathEx
    {
        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(byte value, byte min, byte max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Clamp(decimal value, decimal min, decimal max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Clamp(short value, short min, short max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long value, long min, long max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Clamp(nint value, nint min, nint max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Clamp(sbyte value, sbyte min, sbyte max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Clamp(ushort value, ushort min, ushort max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static uint Clamp(uint value, uint min, uint max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong Clamp(ulong value, ulong min, ulong max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

        /// <summary>Returns <paramref name="value" /> clamped to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</summary>
        /// <param name="value">The value to be clamped.</param>
        /// <param name="min">The lower bound of the result.</param>
        /// <param name="max">The upper bound of the result.</param>
        /// <returns>
        ///   <paramref name="value" /> if <paramref name="min" /> ≤ <paramref name="value" /> ≤ <paramref name="max" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="min" /> if <paramref name="value" /> &lt; <paramref name="min" />.
        ///
        ///   -or-
        ///
        ///   <paramref name="max" /> if <paramref name="max" /> &lt; <paramref name="value" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static nuint Clamp(nuint value, nuint min, nuint max)
#if HAS_CLAMP
            => Math.Clamp(value, min, max);
#else
        {
            if (min > max) {
                ThrowMinMaxException(min, max);
            }

            if (value < min) {
                return min;
            } else if (value > max) {
                return max;
            }

            return value;
        }
#endif

#if !HAS_CLAMP
        [DoesNotReturn]
        private static void ThrowMinMaxException<T>(T min, T max) {
            throw new ArgumentException($"Minimum {min} is less than maximum {max}");
        }
#endif
    }
}
