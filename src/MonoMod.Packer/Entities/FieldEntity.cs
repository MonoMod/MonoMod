using AsmResolver;
using AsmResolver.DotNet;
using System.Diagnostics;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class FieldEntity: FieldEntityBase {
        private string DebuggerDisplay() => Definition.ToString();

        public readonly FieldDefinition Definition;

        public FieldEntity(TypeEntityMap map, FieldDefinition def) : base(map) {
            Definition = def;
        }

        public override Utf8String? Name => Definition.Name;
        public TypeEntity DeclaringType => Map.Lookup(Definition.DeclaringType!);
    }
}