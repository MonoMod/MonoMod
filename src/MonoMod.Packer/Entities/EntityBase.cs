using AsmResolver;

namespace MonoMod.Packer.Entities {
    internal abstract class EntityBase {
        public readonly TypeEntityMap Map;

        protected EntityBase(TypeEntityMap map) {
            Map = map;
        }

        public abstract Utf8String? Name { get; }
    }
}
