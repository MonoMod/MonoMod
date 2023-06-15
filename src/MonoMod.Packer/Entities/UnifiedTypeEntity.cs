using AsmResolver;
using MonoMod.Utils;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class UnifiedTypeEntity : TypeEntityBase {
        private string DebuggerDisplay() => "Unified " + types[0].Definition.FullName;

        private readonly IReadOnlyList<TypeEntity> types;

        public UnifiedTypeEntity(TypeEntityMap map, IReadOnlyList<TypeEntity> types) : base(map) {
            Helpers.Assert(types.Count > 0);
            this.types = types;
        }

        public override Utf8String? Namespace => types[0].Definition.Namespace;
        public override Utf8String? Name => types[0].Definition.Name;

        public new ImmutableArray<UnifiedTypeEntity> NestedTypes => base.NestedTypes.CastArray<UnifiedTypeEntity>();

        protected override ImmutableArray<TypeEntityBase> MakeNestedTypes() {
            var dict = new Dictionary<NullableUtf8String, List<TypeEntity>>();
            foreach (var type in types) {
                foreach (var nested in type.NestedTypes) {
                    Helpers.DAssert(nested.Definition.Namespace is null);
                    if (!dict.TryGetValue(nested.Definition.Name, out var list)) {
                        dict.Add(nested.Definition.Name, list = new());
                    }
                    list.Add(nested);
                }
            }

            return dict.Values
                .Select(l => new UnifiedTypeEntity(Map, l))
                .ToImmutableArray()
                .CastArray<TypeEntityBase>();
        }

        protected override ImmutableArray<MethodEntityBase> MakeStaticMethods() {
            throw new System.NotImplementedException();
        }

        protected override ImmutableArray<MethodEntityBase> MakeInstanceMethods() {
            throw new System.NotImplementedException();
        }

        protected override ImmutableArray<FieldEntityBase> MakeStaticFields() {
            throw new System.NotImplementedException();
        }

        protected override ImmutableArray<FieldEntityBase> MakeInstanceFields() {
            throw new System.NotImplementedException();
        }
    }
}
