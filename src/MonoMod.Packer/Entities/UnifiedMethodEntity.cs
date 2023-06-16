using AsmResolver;
using MonoMod.Utils;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MonoMod.Packer.Entities {
    internal sealed class UnifiedMethodEntity : MethodEntityBase {
        private readonly IReadOnlyList<MethodEntity> methods;

        public UnifiedMethodEntity(TypeEntityMap map, IReadOnlyList<MethodEntity> methods) : base(map) {
            Helpers.DAssert(methods.Count > 0);
#if DEBUG
            var name = methods[0].Name;
            Helpers.DAssert(methods.All(f => f.Name == name));
#endif
            this.methods = methods;
        }

        public override Utf8String? Name => methods[0].Name;

        protected override ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore() {
            throw new System.NotImplementedException();
        }
    }
}
