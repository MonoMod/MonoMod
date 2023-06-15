using AsmResolver.DotNet;
using System.Diagnostics;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class FieldEntity : EntityBase {
        private string DebuggerDisplay() => Definition.ToString();

        public readonly FieldDefinition Definition;

        public FieldEntity(TypeEntityMap map, FieldDefinition def) : base(map) {
            Definition = def;
        }

        public TypeEntity DeclaringType => Map.Lookup(Definition.DeclaringType!);
    }
}