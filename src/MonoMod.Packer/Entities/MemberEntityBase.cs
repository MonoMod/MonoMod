using System.Collections.Immutable;

namespace MonoMod.Packer.Entities {
    internal abstract class MemberEntityBase : EntityBase {
        protected MemberEntityBase(TypeEntityMap map) : base(map) {
        }

        private ImmutableArray<TypeEntityBase> lazyTypesInSignature;
        public ImmutableArray<TypeEntityBase> TypesInSignature {
            get {
                if (lazyTypesInSignature.IsDefault) {
                    ImmutableInterlocked.InterlockedInitialize(
                        ref lazyTypesInSignature,
                        MakeTypesInSignature()
                    );
                }
                return lazyTypesInSignature;
            }
        }

        private ImmutableArray<TypeEntityBase> MakeTypesInSignature() {
            var sigTypes = MakeTypesInSignatureCore();
            return sigTypes.Sort(static (x, y) => x.GetHashCode().CompareTo(y.GetHashCode()));
        }

        protected abstract ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore();
    }
}
