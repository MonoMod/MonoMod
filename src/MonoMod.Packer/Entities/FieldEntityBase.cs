using MonoMod.Packer.Utilities;

namespace MonoMod.Packer.Entities {
    internal abstract class FieldEntityBase : MemberEntityBase {
        protected FieldEntityBase(TypeEntityMap map) : base(map) {
        }

        public bool HasUnifiableInitializer {
            get {
                if (!HasState(EntityInitializationState.HasInitializer)) {
                    SetFlag(EntityFlags.HasUnifiableInitializer, GetHasUnifiableInitializer());
                    MarkState(EntityInitializationState.HasInitializer);
                }
                return GetFlag(EntityFlags.HasUnifiableInitializer);
            }
        }
        protected abstract bool GetHasUnifiableInitializer();

        private FieldInitializer? lazyInitializer;
        public FieldInitializer? Initializer {
            get {
                if (!HasState(EntityInitializationState.Initializer)) {
                    lazyInitializer = GetInitializer();
                    MarkState(EntityInitializationState.Initializer);
                }
                return lazyInitializer;
            }
        }
        protected abstract FieldInitializer? GetInitializer();
    }
}