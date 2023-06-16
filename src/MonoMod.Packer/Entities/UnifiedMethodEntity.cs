using AsmResolver;
using MonoMod.Utils;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class UnifiedMethodEntity : MethodEntityBase {
        private string DebuggerDisplay() => $"Unified {Name}:{FullSig}";

        public readonly string? FullSig;
        private readonly IReadOnlyList<MethodEntity> methods;

        public UnifiedMethodEntity(TypeEntityMap map, IReadOnlyList<MethodEntity> methods) : base(map) {
            Helpers.DAssert(methods.Count > 0);
            FullSig = methods[0].Definition.Signature?.ToString();
#if DEBUG
            var name = methods[0].Name;
            Helpers.DAssert(methods.All(f => f.Name == name
                && f.Definition.Signature?.ToString() == FullSig));
#endif
            this.methods = methods;
        }

        public override Utf8String? Name => methods[0].Name;

        public new ImmutableArray<UnifiedTypeEntity> TypesInSignature => base.TypesInSignature.CastArray<UnifiedTypeEntity>();
        protected override ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore() {
            var set = new HashSet<UnifiedTypeEntity>();
            foreach (var method in methods) {
                foreach (var type in method.TypesInSignature) {
                    _ = set.Add(type.UnifiedType);
                }
            }
            return set.ToImmutableArray().CastArray<TypeEntityBase>();
        }
    }
}
