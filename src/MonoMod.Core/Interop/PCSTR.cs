using System;
using System.Diagnostics;

namespace MonoMod.Core.Interop
{
    /// <summary>
    /// A pointer to a constant character string.
    /// </summary>
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
    internal unsafe readonly partial struct PCSTR
        : IEquatable<PCSTR>
    {
        /// <summary>
        /// A pointer to the first character in the string. The content should be considered readonly, as it was typed as constant in the SDK.
        /// </summary>
        internal readonly byte* Value;
        internal PCSTR(byte* value) => Value = value;
        public static implicit operator byte*(PCSTR value) => value.Value;
        public static explicit operator PCSTR(byte* value) => new(value);

        public bool Equals(PCSTR other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is PCSTR other && Equals(other);

        public override int GetHashCode() => unchecked((int)Value);


        /// <summary>
        /// Gets the number of characters up to the first null character (exclusive).
        /// </summary>
        internal int Length
        {
            get
            {
                var p = Value;
                if (p is null)
                    return 0;
                while (*p != 0)
                    p++;
                return checked((int)(p - Value));
            }
        }


        /// <summary>
        /// Returns a <see langword="string"/> with a copy of this character array, decoding as UTF-8.
        /// </summary>
        /// <returns>A <see langword="string"/>, or <see langword="null"/> if <see cref="Value"/> is <see langword="null"/>.</returns>
        public override string? ToString() => Value is null ? null : new string((sbyte*)Value, 0, Length, System.Text.Encoding.UTF8);


        private string? DebuggerDisplay => ToString();

        /// <summary>
        /// Returns a span of the characters in this string.
        /// </summary>
        internal ReadOnlySpan<byte> AsSpan() => Value is null ? default : new ReadOnlySpan<byte>(Value, Length);
    }
}
