using AsmResolver.DotNet;

namespace MonoMod.Packer.Entities {
    internal sealed class FieldEntity : EntityBase {
        public readonly FieldDefinition Definition;

        public FieldEntity(TypeEntityMap map, FieldDefinition def) : base(map) {
            Definition = def;
        }

        public TypeEntity DeclaringType => Map.Lookup(Definition.DeclaringType!);
    }
}