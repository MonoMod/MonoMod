using MonoMod.Backports;
using MonoMod.Utils;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace MonoMod.Logs
{
    [InterpolatedStringHandler]
    public ref struct DebugLogInterpolatedStringHandler
    {

        // Most of this implementation is copied from DefaultInterpolatedStringHandler so we can get access to the current length

        private const int GuessedLengthPerHole = 11;
        /// <summary>Minimum size array to rent from the pool.</summary>
        /// <remarks>Same as stack-allocation size used today by string.Format.</remarks>
        private const int MinimumArrayPoolLength = 256;

        /// <summary>Array rented from the array pool and used to back <see cref="_chars"/>.</summary>
        private char[]? _arrayToReturnToPool;
        /// <summary>The span to write into.</summary>
        private Span<char> _chars;
        /// <summary>Position at which to write the next character.</summary>
        private int _pos;

        private int holeBegin;

        private int holePos;

        private Memory<MessageHole> holes;

        internal readonly bool enabled;

        public DebugLogInterpolatedStringHandler(int literalLength, int formattedCount, bool enabled, bool recordHoles, out bool isEnabled)
        {
            _pos = holeBegin = holePos = 0;
            this.enabled = isEnabled = enabled;
            if (enabled)
            {
                _chars = _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
                if (recordHoles)
                {
                    holes = new MessageHole[formattedCount];
                }
                else
                {
                    holes = default;
                }
            }
            else
            {
                _chars = _arrayToReturnToPool = null;
                holes = default;
            }
        }

        public DebugLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
        {
            var log = DebugLog.Instance;
            _pos = holeBegin = holePos = 0;
            if (log.ShouldLog)
            {
                enabled = isEnabled = true;
                _chars = _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
                if (log.RecordHoles)
                {
                    holes = new MessageHole[formattedCount];
                }
                else
                {
                    holes = default;
                }
            }
            else
            {
                enabled = isEnabled = false;
                _chars = _arrayToReturnToPool = null;
                holes = default;
            }
        }

        public DebugLogInterpolatedStringHandler(int literalLength, int formattedCount, LogLevel level, out bool isEnabled)
        {
            var log = DebugLog.Instance;
            _pos = holeBegin = holePos = 0;
            if (log.ShouldLogLevel(level))
            {
                enabled = isEnabled = true;
                _chars = _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
                if (log.ShouldLevelRecordHoles(level))
                {
                    holes = new MessageHole[formattedCount];
                }
                else
                {
                    holes = default;
                }
            }
            else
            {
                enabled = isEnabled = false;
                _chars = _arrayToReturnToPool = null;
                holes = default;
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)] // becomes a constant when inputs are constant
        internal static int GetDefaultLength(int literalLength, int formattedCount) =>
            Math.Max(MinimumArrayPoolLength, literalLength + (formattedCount * GuessedLengthPerHole));

        internal ReadOnlySpan<char> Text => _chars.Slice(0, _pos);

        public override string ToString() => Text.ToString();

        public string ToStringAndClear()
        {
            var result = Text.ToString();
            Clear();
            return result;
        }

        internal string ToStringAndClear(out ReadOnlyMemory<MessageHole> holes)
        {
            holes = this.holes;
            return ToStringAndClear();
        }

        /// <summary>Clears the handler, returning any rented array to the pool.</summary>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)] // used only on a few hot paths
        internal void Clear()
        {
            var toReturn = _arrayToReturnToPool;
            this = default; // defensive clear
            if (toReturn is not null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "The value.Length cases are expected to be JIT-time constants due to inlining, and doing argument verification may interfere with that.")]
        public void AppendLiteral(string value)
        {
            if (value.Length == 1)
            {
                var chars = _chars;
                var pos = _pos;
                if ((uint)pos < (uint)chars.Length)
                {
                    chars[pos] = value[0];
                    _pos = pos + 1;
                }
                else
                {
                    GrowThenCopyString(value);
                }
                return;
            }

            if (value.Length == 2)
            {
                var chars = _chars;
                var pos = _pos;
                if ((uint)pos < chars.Length - 1)
                {
                    value.AsSpan().CopyTo(chars.Slice(pos));
                    _pos = pos + 2;
                }
                else
                {
                    GrowThenCopyString(value);
                }
                return;
            }

            AppendStringDirect(value);
        }

        private void AppendStringDirect(string value)
        {
            if (value.AsSpan().TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                GrowThenCopyString(value);
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private void BeginHole()
        {
            holeBegin = _pos;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private void EndHole(object? obj, bool reprd)
            => EndHole<object?>(obj, reprd);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EndHole<T>(in T obj, bool reprd)
        {
            if (!holes.IsEmpty)
            {
                holes.Span[holePos++] = reprd ? new MessageHole(holeBegin, _pos, obj) : new(holeBegin, _pos);
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted(string? value)
        {
            BeginHole();
            if (value is not null &&
                value.AsSpan().TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                AppendFormattedSlow(value);
            }
            EndHole(value, true);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendFormattedSlow(string? value)
        {
            if (value is not null)
            {
                EnsureCapacityForAdditionalChars(value.Length);
                value.AsSpan().CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted(string? value, int alignment = 0, string? format = default)
            => AppendFormatted<string?>(value, alignment, format);

        public void AppendFormatted(ReadOnlySpan<char> value)
        {
            BeginHole();
            // Fast path for when the value fits in the current buffer
            if (value.TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                GrowThenCopySpan(value);
            }
            EndHole(null, false);
        }

        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = default)
        {
            var leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            var paddingRequired = alignment - value.Length;
            if (paddingRequired <= 0)
            {
                // The value is as large or larger than the required amount of padding,
                // so just write the value.
                AppendFormatted(value);
                return;
            }

            BeginHole();
            // Write the value along with the appropriate padding.
            EnsureCapacityForAdditionalChars(value.Length + paddingRequired);
            if (leftAlign)
            {
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
                _chars.Slice(_pos, paddingRequired).Fill(' ');
                _pos += paddingRequired;
            }
            else
            {
                _chars.Slice(_pos, paddingRequired).Fill(' ');
                _pos += paddingRequired;
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
            }
            EndHole(null, false);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0038:Use pattern matching",
            Justification = "We want to avoid boxing here as much as possible, and the JIT doesn't recognize pattern matching to prevent that." +
                            "Not that the compiler emits a constrained call here anyway, but...")]
        public void AppendFormatted<T>(T value)
        {
            if (typeof(T) == typeof(IntPtr))
            {
                AppendFormatted(Unsafe.As<T, IntPtr>(ref value));
                return;
            }
            if (typeof(T) == typeof(UIntPtr))
            {
                AppendFormatted(Unsafe.As<T, UIntPtr>(ref value));
                return;
            }

            BeginHole();
            string? s;
            if (DebugFormatter.CanDebugFormat(value, out var dbgFormatExtraData))
            {
                int wrote;
                while (!DebugFormatter.TryFormatInto(value, dbgFormatExtraData, _chars.Slice(_pos), out wrote))
                    Grow();
                _pos += wrote;
                return;
            }
            else if (value is IFormattable)
            {
                s = ((IFormattable)value).ToString(format: null, null); // constrained call avoiding boxing for value types (though it might box later anyway
            }
            else
            {
                s = value?.ToString();
            }

            if (s is not null)
            {
                AppendStringDirect(s);
            }
            EndHole(value, true);
        }


        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private void AppendFormatted(IntPtr value)
        {
            unchecked
            {
                if (IntPtr.Size == 4)
                {
                    AppendFormatted((int)value);
                }
                else
                {
                    AppendFormatted((long)value);
                }
            }
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private void AppendFormatted(IntPtr value, string? format)
        {
            unchecked
            {
                if (IntPtr.Size == 4)
                {
                    AppendFormatted((int)value, format);
                }
                else
                {
                    AppendFormatted((long)value, format);
                }
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private void AppendFormatted(UIntPtr value)
        {
            unchecked
            {
                if (UIntPtr.Size == 4)
                {
                    AppendFormatted((uint)value);
                }
                else
                {
                    AppendFormatted((ulong)value);
                }
            }
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private void AppendFormatted(UIntPtr value, string? format)
        {
            unchecked
            {
                if (UIntPtr.Size == 4)
                {
                    AppendFormatted((uint)value, format);
                }
                else
                {
                    AppendFormatted((ulong)value, format);
                }
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted<T>(T value, int alignment)
        {
            var startingPos = _pos;
            AppendFormatted(value);
            if (alignment != 0)
            {
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0038:Use pattern matching",
            Justification = "We want to avoid boxing here as much as possible, and the JIT doesn't recognize pattern matching to prevent that." +
                            "Not that the compiler emits a constrained call here anyway, but...")]
        public void AppendFormatted<T>(T value, string? format)
        {
            if (typeof(T) == typeof(IntPtr))
            {
                AppendFormatted(Unsafe.As<T, IntPtr>(ref value), format);
                return;
            }
            if (typeof(T) == typeof(UIntPtr))
            {
                AppendFormatted(Unsafe.As<T, UIntPtr>(ref value), format);
                return;
            }

            BeginHole();
            string? s;
            if (DebugFormatter.CanDebugFormat(value, out var dbgFormatExtraData))
            {
                int wrote;
                while (!DebugFormatter.TryFormatInto(value, dbgFormatExtraData, _chars.Slice(_pos), out wrote))
                    Grow();
                _pos += wrote;
                return;
            }
            else if (value is IFormattable)
            {
                // If the value can format itself directly into our buffer, do so.
                /*if (value is ISpanFormattable) {
                    int charsWritten;
                    while (!((ISpanFormattable) value).TryFormat(_chars.Slice(_pos), out charsWritten, format, _provider)) // constrained call avoiding boxing for value types
                    {
                        Grow();
                    }

                    _pos += charsWritten;
                    return;
                }*/

                s = ((IFormattable)value).ToString(format, null); // constrained call avoiding boxing for value types
            }
            else
            {
                s = value?.ToString();
            }

            if (s is not null)
            {
                AppendStringDirect(s);
            }
            EndHole(value, true);
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            var startingPos = _pos;
            AppendFormatted(value, format);
            if (alignment != 0)
            {
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }

        /// <summary>Handles adding any padding required for aligning a formatted value in an interpolation expression.</summary>
        /// <param name="startingPos">The position at which the written value started.</param>
        /// <param name="alignment">Non-zero minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
        private void AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
        {
            Helpers.DAssert(startingPos >= 0 && startingPos <= _pos);
            Helpers.DAssert(alignment != 0);

            var charsWritten = _pos - startingPos;

            var leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            var paddingNeeded = alignment - charsWritten;
            if (paddingNeeded > 0)
            {
                EnsureCapacityForAdditionalChars(paddingNeeded);

                if (leftAlign)
                {
                    _chars.Slice(_pos, paddingNeeded).Fill(' ');
                }
                else
                {
                    _chars.Slice(startingPos, charsWritten).CopyTo(_chars.Slice(startingPos + paddingNeeded));
                    _chars.Slice(startingPos, paddingNeeded).Fill(' ');
                }

                _pos += paddingNeeded;
            }
        }

        /// <summary>Ensures <see cref="_chars"/> has the capacity to store <paramref name="additionalChars"/> beyond <see cref="_pos"/>.</summary>
        [MethodImpl(MonoMod.Backports.MethodImplOptionsEx.AggressiveInlining)]
        private void EnsureCapacityForAdditionalChars(int additionalChars)
        {
            if (_chars.Length - _pos < additionalChars)
            {
                Grow(additionalChars);
            }
        }

        /// <summary>Fallback for fast path in <see cref="AppendStringDirect"/> when there's not enough space in the destination.</summary>
        /// <param name="value">The string to write.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowThenCopyString(string value)
        {
            Grow(value.Length);
            value.AsSpan().CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        /// <summary>Fallback for <see cref="AppendFormatted(ReadOnlySpan{char})"/> for when not enough space exists in the current buffer.</summary>
        /// <param name="value">The span to write.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowThenCopySpan(ReadOnlySpan<char> value)
        {
            Grow(value.Length);
            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        /// <summary>Grows <see cref="_chars"/> to have the capacity to store at least <paramref name="additionalChars"/> beyond <see cref="_pos"/>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
        private void Grow(int additionalChars)
        {
            // This method is called when the remaining space (_chars.Length - _pos) is
            // insufficient to store a specific number of additional characters.  Thus, we
            // need to grow to at least that new total. GrowCore will handle growing by more
            // than that if possible.
            Helpers.DAssert(additionalChars > _chars.Length - _pos);
            GrowCore((uint)_pos + (uint)additionalChars);
        }

        /// <summary>Grows the size of <see cref="_chars"/>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
        private void Grow()
        {
            // This method is called when the remaining space in _chars isn't sufficient to continue
            // the operation.  Thus, we need at least one character beyond _chars.Length.  GrowCore
            // will handle growing by more than that if possible.
            GrowCore((uint)_chars.Length + 1);
        }

        /// <summary>Grow the size of <see cref="_chars"/> to at least the specified <paramref name="requiredMinCapacity"/>.</summary>
        [MethodImpl(MonoMod.Backports.MethodImplOptionsEx.AggressiveInlining)] // but reuse this grow logic directly in both of the above grow routines
        private void GrowCore(uint requiredMinCapacity)
        {
            // We want the max of how much space we actually required and doubling our capacity (without going beyond the max allowed length). We
            // also want to avoid asking for small arrays, to reduce the number of times we need to grow, and since we're working with unsigned
            // ints that could technically overflow if someone tried to, for example, append a huge string to a huge string, we also clamp to int.MaxValue.
            // Even if the array creation fails in such a case, we may later fail in ToStringAndClear.

            var newCapacity = Math.Max(requiredMinCapacity, Math.Min((uint)_chars.Length * 2, uint.MaxValue));
            var arraySize = (int)MathEx.Clamp(newCapacity, MinimumArrayPoolLength, int.MaxValue);

            var newArray = ArrayPool<char>.Shared.Rent(arraySize);
            _chars.Slice(0, _pos).CopyTo(newArray);

            var toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = newArray;

            if (toReturn is not null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }
    }
}
