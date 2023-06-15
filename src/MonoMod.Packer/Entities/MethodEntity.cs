using AsmResolver;
using AsmResolver.DotNet;
using System.Diagnostics;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class MethodEntity : MethodEntityBase {
        private string DebuggerDisplay() => Definition.ToString();

        public readonly MethodDefinition Definition;

        public MethodEntity(TypeEntityMap map, MethodDefinition def) : base(map) {
            Definition = def;
        }

        public override Utf8String? Name => Definition.Name;

        public TypeEntity DeclaringType => Map.Lookup(Definition.DeclaringType!);
    }
}
