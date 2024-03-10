using MonoMod.Backports;
using MonoMod.Logs;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Utils
{
    /// <summary>
    /// The kind of an address in a <see cref="BytePattern"/>.
    /// </summary>
    [Flags]
    [SuppressMessage("Design", "CA1008:Enums should have zero value",
        Justification = "There is no sensible meaning to a 'None' value, and Rel32 is a reasonable default.")]
    public enum AddressKind
    {
        /// <summary>
        /// A 32-bit relative address.
        /// </summary>
        Rel32 = 0b00,
        /// <summary>
        /// A 64-bit relative address.
        /// </summary>
        Rel64 = 0b10,
        /// <summary>
        /// A 32-bit absolute address.
        /// </summary>
        Abs32 = 0b01,
        /// <summary>
        /// A 64-bit absolute address.
        /// </summary>
        Abs64 = 0b11,
        /// <summary>
        /// A <see cref="Rel32"/> address, pointing to <c>PrecodeFixupThunk</c> or some equivalent.
        /// </summary>
        PrecodeFixupThunkRel32 = 0b100,
        /// <summary>
        /// A <see cref="Rel64"/> address, pointing to <c>PrecodeFixupThunk</c> or some equivalent.
        /// </summary>
        PrecodeFixupThunkRel64 = 0b110,
        /// <summary>
        /// A <see cref="Abs32"/> address, pointing to <c>PrecodeFixupThunk</c> or some equivalent.
        /// </summary>
        PrecodeFixupThunkAbs32 = 0b101,
        /// <summary>
        /// A <see cref="Abs64"/> address, pointing to <c>PrecodeFixupThunk</c> or some equivalent.
        /// </summary>
        PrecodeFixupThunkAbs64 = 0b111,
        /// <summary>
        /// An indirect address. This must be combined with one of the other address kinds. The address points
        /// to a word-sized indirection cell which contains the actual target.
        /// </summary>
        Indirect = 0b1000,
    }

    /// <summary>
    /// Extensions to <see cref="AddressKind"/>.
    /// </summary>
    public static class AddressKindExtensions
    {

        /// <summary>
        /// The <see cref="AddressKind"/> flag indicating that the kind is absolute.
        /// </summary>
        public const AddressKind IsAbsoluteField = (AddressKind)0b01;
        /// <summary>
        /// The <see cref="AddressKind"/> flag indicating that the kind is 64-bit.
        /// </summary>
        public const AddressKind Is64BitField = (AddressKind)0b10;
        /// <summary>
        /// The <see cref="AddressKind"/> flag indicating that the kind is a <c>PrecodeFixupThunk</c> address.
        /// </summary>
        public const AddressKind IsPrecodeFixupField = (AddressKind)0b100;
        /// <summary>
        /// The <see cref="AddressKind"/> flag indicating that the kind is indirect.
        /// </summary>
        public const AddressKind IsIndirectField = (AddressKind)0b1000;

        /// <summary>
        /// Gets whether or not this <see cref="AddressKind"/> is relative.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="value"/> is relative; <see langword="false"/> otherwise.</returns>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsRelative(this AddressKind value)
            => (value & IsAbsoluteField) == 0;
        /// <summary>
        /// Gets whether or not this <see cref="AddressKind"/> is absolute.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="value"/> is absolute; <see langword="false"/> otherwise.</returns>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsAbsolute(this AddressKind value)
            => !value.IsRelative();
        /// <summary>
        /// Gets whether or not this <see cref="AddressKind"/> is 32-bit.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="value"/> is 32-bit; <see langword="false"/> otherwise.</returns>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool Is32Bit(this AddressKind value)
            => (value & Is64BitField) == 0;
        /// <summary>
        /// Gets whether or not this <see cref="AddressKind"/> is 64-bit.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="value"/> is 64-bit; <see langword="false"/> otherwise.</returns>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool Is64Bit(this AddressKind value)
            => !value.Is32Bit();

        /// <summary>
        /// Gets whether or not this <see cref="AddressKind"/> is a <c>PrecodeFixupThunk</c> address.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="value"/> is a <c>PrecodeFixupThunk</c> address; <see langword="false"/> otherwise.</returns>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsPrecodeFixup(this AddressKind value)
            => (value & IsPrecodeFixupField) != 0;

        /// <summary>
        /// Gets whether or not this <see cref="AddressKind"/> is indirect.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="value"/> is indirect; <see langword="false"/> otherwise.</returns>
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsIndirect(this AddressKind value)
            => (value & IsIndirectField) != 0;

        /// <summary>
        /// Validates <paramref name="value"/>, ensuring that it is a valid <see cref="AddressKind"/>.
        /// </summary>
        /// <param name="value">The value to validate.</param>
        /// <param name="argName">The name of the argument.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="value"/> is invalid.</exception>
        public static void Validate(this AddressKind value, [CallerArgumentExpression("value")] string argName = "")
        {
            if ((value & ~(IsAbsoluteField | Is64BitField | IsPrecodeFixupField | IsIndirectField)) != 0)
                throw new ArgumentOutOfRangeException(argName);
        }

        /// <summary>
        /// Converts this <see cref="AddressKind"/> to a string.
        /// </summary>
        /// <param name="value">The <see cref="AddressKind"/> to convert.</param>
        /// <returns>The string representation of this <see cref="AddressKind"/>.</returns>
        public static string FastToString(this AddressKind value)
            => DebugFormatter.Format( // Use DebugFormatter to force the use of an InterpolatedStringHandler instead of String.Concat
                $"{(value.IsPrecodeFixup() ? "PrecodeFixupThunk" : "")}{(value.IsRelative() ? "Rel" : "Abs")}" +
                $"{(value.Is32Bit() ? "32" : "64")}{(value.IsIndirect() ? "Indirect" : "")}"
            );
        // We need to avoid String.Concat where possible to avoid deadlocks when MonoMod is used to detour String.Concat
    }
}
