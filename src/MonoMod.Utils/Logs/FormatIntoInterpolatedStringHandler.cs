using MonoMod.Backports;
using MonoMod.Utils;
using System;
using System.Runtime.CompilerServices;

namespace MonoMod.Logs
{
    [InterpolatedStringHandler]
    public ref struct FormatIntoInterpolatedStringHandler
    {
        private readonly Span<char> _chars;
        internal int pos;
        internal bool incomplete;

        public FormatIntoInterpolatedStringHandler(int literalLen, int numHoles, Span<char> into, out bool enabled)
        {
            _chars = into;
            pos = 0;
            if (into.Length < literalLen)
            {
                incomplete = true;
                enabled = false;
            }
            else
            {
                incomplete = false;
                enabled = true;
            }
        }


        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "The value.Length cases are expected to be JIT-time constants due to inlining, and doing argument verification may interfere with that.")]
        public bool AppendLiteral(string value)
        {
            if (value.Length == 1)
            {
                var chars = _chars;
                var pos = this.pos;
                if ((uint)pos < (uint)chars.Length)
                {
                    chars[pos] = value[0];
                    this.pos = pos + 1;
                    return true;
                }
                else
                {
                    incomplete = true;
                    return false;
                }
            }

            if (value.Length == 2)
            {
                var chars = _chars;
                var pos = this.pos;
                if ((uint)pos < chars.Length - 1)
                {
                    value.AsSpan().CopyTo(chars.Slice(pos));
                    this.pos = pos + 2;
                    return true;
                }
                else
                {
                    incomplete = true;
                    return false;
                }
            }

            return AppendStringDirect(value);
        }

        private bool AppendStringDirect(string value)
        {
            if (value.AsSpan().TryCopyTo(_chars.Slice(pos)))
            {
                pos += value.Length;
                return true;
            }
            else
            {
                incomplete = true;
                return false;
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public bool AppendFormatted(string? value)
        {
            if (value is null)
                return true;

            if (value.AsSpan().TryCopyTo(_chars.Slice(pos)))
            {
                pos += value.Length;
                return true;
            }
            else
            {
                incomplete = true;
                return false;
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public bool AppendFormatted(string? value, int alignment = 0, string? format = default)
            => AppendFormatted<string?>(value, alignment, format);

        public bool AppendFormatted(ReadOnlySpan<char> value)
        {
            if (value.TryCopyTo(_chars.Slice(pos)))
            {
                pos += value.Length;
                return true;
            }
            else
            {
                incomplete = true;
                return false;
            }
        }

        public bool AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = default)
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
                return AppendFormatted(value);
            }

            if (_chars.Slice(pos).Length < value.Length + paddingRequired)
            {
                incomplete = true;
                return false;
            }

            // Write the value along with the appropriate padding.
            if (leftAlign)
            {
                value.CopyTo(_chars.Slice(pos));
                pos += value.Length;
                _chars.Slice(pos, paddingRequired).Fill(' ');
                pos += paddingRequired;
            }
            else
            {
                _chars.Slice(pos, paddingRequired).Fill(' ');
                pos += paddingRequired;
                value.CopyTo(_chars.Slice(pos));
                pos += value.Length;
            }

            return true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0038:Use pattern matching",
            Justification = "We want to avoid boxing here as much as possible, and the JIT doesn't recognize pattern matching to prevent that." +
                            "Not that the compiler emits a constrained call here anyway, but...")]
        public bool AppendFormatted<T>(T value)
        {
            if (typeof(T) == typeof(IntPtr))
            {
                return AppendFormatted(Unsafe.As<T, IntPtr>(ref value));
            }
            if (typeof(T) == typeof(UIntPtr))
            {
                return AppendFormatted(Unsafe.As<T, UIntPtr>(ref value));
            }

            string? s;
            if (DebugFormatter.CanDebugFormat(value, out var dbgFormatExtraData))
            {
                if (!DebugFormatter.TryFormatInto(value, dbgFormatExtraData, _chars.Slice(pos), out var wrote))
                {
                    incomplete = true;
                    return false;
                }
                pos += wrote;
                return true;
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
                return AppendStringDirect(s);
            }

            return true;
        }


        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private bool AppendFormatted(IntPtr value)
        {
            unchecked
            {
                if (IntPtr.Size == 4)
                {
                    return AppendFormatted((int)value);
                }
                else
                {
                    return AppendFormatted((long)value);
                }
            }
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private bool AppendFormatted(IntPtr value, string? format)
        {
            unchecked
            {
                if (IntPtr.Size == 4)
                {
                    return AppendFormatted((int)value, format);
                }
                else
                {
                    return AppendFormatted((long)value, format);
                }
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private bool AppendFormatted(UIntPtr value)
        {
            unchecked
            {
                if (UIntPtr.Size == 4)
                {
                    return AppendFormatted((uint)value);
                }
                else
                {
                    return AppendFormatted((ulong)value);
                }
            }
        }
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private bool AppendFormatted(UIntPtr value, string? format)
        {
            unchecked
            {
                if (UIntPtr.Size == 4)
                {
                    return AppendFormatted((uint)value, format);
                }
                else
                {
                    return AppendFormatted((ulong)value, format);
                }
            }
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public bool AppendFormatted<T>(T value, int alignment)
        {
            var startingPos = pos;
            if (!AppendFormatted(value))
                return false;
            if (alignment != 0)
            {
                return AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
            return true;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0038:Use pattern matching",
            Justification = "We want to avoid boxing here as much as possible, and the JIT doesn't recognize pattern matching to prevent that." +
                            "Not that the compiler emits a constrained call here anyway, but...")]
        public bool AppendFormatted<T>(T value, string? format)
        {
            if (typeof(T) == typeof(IntPtr))
            {
                return AppendFormatted(Unsafe.As<T, IntPtr>(ref value), format);
            }
            if (typeof(T) == typeof(UIntPtr))
            {
                return AppendFormatted(Unsafe.As<T, UIntPtr>(ref value), format);
            }

            string? s;
            if (DebugFormatter.CanDebugFormat(value, out var dbgFormatExtraData))
            {
                if (!DebugFormatter.TryFormatInto(value, dbgFormatExtraData, _chars.Slice(pos), out var wrote))
                {
                    incomplete = true;
                    return false;
                }
                pos += wrote;
                return true;
            }
            else if (value is IFormattable)
            {
                s = ((IFormattable)value).ToString(format, null); // constrained call avoiding boxing for value types
            }
            else
            {
                s = value?.ToString();
            }

            if (s is not null)
            {
                return AppendStringDirect(s);
            }
            return true;
        }

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public bool AppendFormatted<T>(T value, int alignment, string? format)
        {
            var startingPos = pos;
            if (!AppendFormatted(value, format))
                return false;
            if (alignment != 0)
            {
                return AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
            return true;
        }

        /// <summary>Handles adding any padding required for aligning a formatted value in an interpolation expression.</summary>
        /// <param name="startingPos">The position at which the written value started.</param>
        /// <param name="alignment">Non-zero minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
        private bool AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
        {
            Helpers.DAssert(startingPos >= 0 && startingPos <= pos);
            Helpers.DAssert(alignment != 0);

            var charsWritten = pos - startingPos;

            var leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            var paddingNeeded = alignment - charsWritten;
            if (paddingNeeded > 0)
            {
                if (_chars.Slice(pos).Length < paddingNeeded)
                {
                    incomplete = true;
                    return false;
                }

                if (leftAlign)
                {
                    _chars.Slice(pos, paddingNeeded).Fill(' ');
                }
                else
                {
                    _chars.Slice(startingPos, charsWritten).CopyTo(_chars.Slice(startingPos + paddingNeeded));
                    _chars.Slice(startingPos, paddingNeeded).Fill(' ');
                }

                pos += paddingNeeded;
            }
            return true;
        }

    }
}