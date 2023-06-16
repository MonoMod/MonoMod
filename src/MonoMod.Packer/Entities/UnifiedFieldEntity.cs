using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using AsmResolver;
using MonoMod.Utils;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class UnifiedFieldEntity : FieldEntityBase {
        private string DebuggerDisplay() => $"Unified field {Name}";

        private readonly IReadOnlyList<FieldEntity> fields;

        public UnifiedFieldEntity(TypeEntityMap map, IReadOnlyList<FieldEntity> fields) : base(map) {
            Helpers.DAssert(fields.Count > 0);
#if DEBUG
            var name = fields[0].Name;
            Helpers.DAssert(fields.All(f => f.Name == name));
#endif
            this.fields = fields;
        }

        public override Utf8String? Name => fields[0].Name;

        public new ImmutableArray<UnifiedTypeEntity> TypesInSignature => base.TypesInSignature.CastArray<UnifiedTypeEntity>();

        protected override ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore() {
            var set = new HashSet<UnifiedTypeEntity>();
            foreach (var field in fields) {
                foreach (var type in field.TypesInSignature) {
                    _ = set.Add(type.UnifiedType);
                }
            }
            return set.ToImmutableArray().CastArray<TypeEntityBase>();
        }
    }
}
