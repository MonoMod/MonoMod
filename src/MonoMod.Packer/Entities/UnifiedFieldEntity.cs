using System.Collections.Generic;
using System.Linq;
using AsmResolver;
using MonoMod.Utils;

namespace MonoMod.Packer.Entities {
    internal sealed class UnifiedFieldEntity : FieldEntityBase {
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
    }
}
