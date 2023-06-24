using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Utilities;
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

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules() {
            var builder = ImmutableArray.CreateBuilder<ModuleDefinition>();
            foreach (var field in fields) {
                builder.AddRange(field.ContributingModules);
            }
            return builder.ToImmutable();
        }

        private FieldInitializer? lazyInitializer;
        protected override bool GetHasUnifiableInitializer() {
            var hasEnumeratedOnce = false;
            FieldInitializer? initializer = null;
            foreach (var field in fields) {
                if (!field.HasUnifiableInitializer) {
                    return false;
                }

                if (!hasEnumeratedOnce) {
                    initializer = field.Initializer;
                } else if (field.Initializer != initializer) {
                    // different initializers
                    return false;
                }

                hasEnumeratedOnce = true;
            }
            lazyInitializer = initializer;
            return true;
        }

        protected override FieldInitializer? GetInitializer() {
            // we have to check HasUnifiableInitializer because it does the actual unification work
            if (!HasUnifiableInitializer)
                return null;
            return lazyInitializer;
        }
    }
}
