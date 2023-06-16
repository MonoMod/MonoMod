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
                        MakeTypesInSignatureCore()
                    );
                }
                return lazyTypesInSignature;
            }
        }

        protected abstract ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore();
    }
}
