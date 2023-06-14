using AsmResolver.DotNet;
using MonoMod.Utils;
using System;

namespace MonoMod.Packer.Entities {
    internal sealed class MethodEntity : EntityBase {
        public readonly MethodDefinition Definition;

        public MethodEntity(TypeEntityMap map, MethodDefinition def) : base(map) {
            Definition = def;
        }

        public TypeEntity DeclaringType => Map.Lookup(Definition.DeclaringType!);

        public bool IsMergeCandidate(MethodEntity other) {
            Helpers.DAssert(DeclaringType == other.DeclaringType);

            if (Definition.Name != other.Definition.Name)
                return false;

            throw new NotImplementedException();
        }
    }
}
