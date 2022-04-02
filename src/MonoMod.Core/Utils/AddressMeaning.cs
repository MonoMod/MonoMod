using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Utils {
    public readonly struct AddressMeaning : IEquatable<AddressMeaning> {
        public AddressKind Kind { get; }
        public int RelativeToOffset { get; }

        public AddressMeaning(AddressKind kind) {
            kind.Validate();
            if (!kind.IsAbsolute())
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            RelativeToOffset = 0;
        }

        public AddressMeaning(AddressKind kind, int relativeOffset) {
            kind.Validate();
            if (!kind.IsRelative())
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (relativeOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(relativeOffset));
            Kind = kind;
            RelativeToOffset = relativeOffset;
        }

        public override bool Equals(object? obj) {
            return obj is AddressMeaning meaning && Equals(meaning);
        }

        public bool Equals(AddressMeaning other) {
            return Kind == other.Kind &&
                   RelativeToOffset == other.RelativeToOffset;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Kind, RelativeToOffset);
        }

        public static bool operator ==(AddressMeaning left, AddressMeaning right) => left.Equals(right);
        public static bool operator !=(AddressMeaning left, AddressMeaning right) => !(left == right);
    }
}
