using AsmResolver.DotNet;
using MonoMod.Utils;
using System;
using System.Diagnostics;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class MethodEntity : EntityBase {
        private string DebuggerDisplay() => Definition.ToString();

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
