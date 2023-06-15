using System.Diagnostics;
using System.Text;

namespace MonoMod.Packer {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal readonly record struct ThreeState {
        private readonly string DebuggerDisplay() => ToString();

        private readonly int value;

        private ThreeState(int value) => this.value = value;

        private const int VYes = 1;
        private const int VNo = 2;
        private const int VMaybe = 0;

        public static readonly ThreeState Yes = new(VYes);
        public static readonly ThreeState No = new(VNo);
        public static readonly ThreeState Maybe = new(VMaybe);

        private readonly bool PrintMembers(StringBuilder stringBuilder) {
            _ = stringBuilder.Append(value switch {
                VYes => "Yes",
                VNo => "No",
                VMaybe => "Maybe",
                _ => $"Invalid ({value})"
            });
            return true;
        }

        public static implicit operator ThreeState(bool v)
            => v ? Yes : No;

        public static bool operator true(ThreeState s) => s == Yes;
        public static bool operator false(ThreeState s) => s == No;

        public bool MaybeYes => this == Yes || this == Maybe;
        public bool MaybeNo => this == No || this == Maybe;

        public static ThreeState operator &(ThreeState l, ThreeState r)
            => (l.value, r.value) switch {
                (VYes, VYes) => Yes, // Yes && Yes == Yes
                (VNo, _) or (_, VNo) => No, // X && No == No && X == No
                (VMaybe, _) or (_, VMaybe) => Maybe, // Maybe && Yes == Yes && Maybe == Maybe (No is covered above)
                _ => Maybe, // should never be reached
            };

        public static ThreeState operator |(ThreeState l, ThreeState r)
            => (l.value, r.value) switch {
                (VYes, _) or (_, VYes) => Yes, // Yes || X == X || Yes == Yes
                (VMaybe, _) or (_, VMaybe) => Maybe, // Maybe || X == X || Maybe == Maybe
                _ => No, //only remaining case is No || No
            };

        public static ThreeState operator !(ThreeState v)
            => v.value switch {
                VYes => No,
                VNo => Yes,
                _ => Maybe,
            };
    }
}
