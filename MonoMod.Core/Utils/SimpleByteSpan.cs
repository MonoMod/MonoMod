using System;

namespace MonoMod.Core.Utils {
    public readonly unsafe ref struct SimpleByteSpan {
        public byte* Start { get; }
        public int Length { get; }

        public SimpleByteSpan(byte* start, int length) {
            Start = start;
            Length = length;
        }

        public byte this[int index] {
            get {
                if (index < 0 || index >= Length)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return Start[index];
            }
            // don't allow setting, as this will primarily be used for things that are readonly
            // if a user wants to write, they still can through the Start pointer
        }

        public SimpleByteSpan Slice(int position) => Slice(position, Length - position);
        public SimpleByteSpan Slice(int position, int length) {
            if (position < 0 || position > Length)
                throw new ArgumentOutOfRangeException(nameof(position));
            if (length < 0 || position + length > Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0)
                return default;

            return new SimpleByteSpan(Start + position, length);
        }
    }
}
