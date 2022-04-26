using MonoMod.Backports;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Utils {
    [Flags]
    [SuppressMessage("Design", "CA1008:Enums should have zero value",
        Justification = "There is no sensible meaning to a 'None' value, and Rel32 is a reasonable default.")]
    public enum AddressKind {
        Rel32 = 0b00,
        Rel64 = 0b10,
        Abs32 = 0b01,
        Abs64 = 0b11,
        PrecodeFixupThunkRel32 = 0b100,
        PrecodeFixupThunkRel64 = 0b110,
        PrecodeFixupThunkAbs32 = 0b101,
        PrecodeFixupThunkAbs64 = 0b111,
    }

    public static class AddressKindExtensions {
        public const AddressKind
            IsAbsoluteField = (AddressKind) 0b01,
            Is64BitField = (AddressKind) 0b10,
            IsPrecodeFixupField = (AddressKind) 0b100;

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsRelative(this AddressKind value)
            => (value & IsAbsoluteField) == 0;
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsAbsolute(this AddressKind value)
            => !value.IsRelative();
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool Is32Bit(this AddressKind value)
            => (value & Is64BitField) == 0;
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool Is64Bit(this AddressKind value)
            => !value.Is32Bit();

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        public static bool IsPrecodeFixup(this AddressKind value)
            => (value & IsPrecodeFixupField) != 0;

        public static void Validate(this AddressKind value, [CallerArgumentExpression("value")] string argName = "") {
            if ((value & ~(IsAbsoluteField | Is64BitField | IsPrecodeFixupField)) != 0)
                throw new ArgumentOutOfRangeException(argName);
        }
    }
}
