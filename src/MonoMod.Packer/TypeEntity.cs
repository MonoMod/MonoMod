using AsmResolver.DotNet;
using MonoMod.Utils;
using System;

namespace MonoMod.Packer {
    internal sealed class TypeEntity {
        public readonly TypeEntityMap Map;
        public readonly TypeDefinition Definition;

        public TypeEntity(TypeEntityMap map, TypeDefinition def) {
            Map = map;
            Definition = def;
        }
    }
}
