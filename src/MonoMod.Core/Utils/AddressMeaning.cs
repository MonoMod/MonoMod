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

        private static nint DoProcessAddress(AddressKind kind, nint basePtr, int offset, ulong address) {
            if (kind.IsAbsolute()) {
                return (nint) address;
            } else { // IsRelative
                var offs = kind.Is32Bit()
                    ? Unsafe.As<ulong, int>(ref address)
                    : Unsafe.As<ulong, long>(ref address);
                return (nint) (basePtr + offset + offs);
            }
        }

        public nint ProcessAddress(nint basePtr, int offset, ulong address) {
            return DoProcessAddress(Kind, basePtr, offset + RelativeToOffset, address);
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
