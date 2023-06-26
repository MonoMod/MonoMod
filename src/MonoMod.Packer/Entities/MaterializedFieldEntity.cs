using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Utilities;
using System;
using System.Collections.Immutable;

namespace MonoMod.Packer.Entities {
    internal sealed class MaterializedFieldEntity : FieldEntityBase {
        public UnifiedFieldEntity Unified { get; }
        public ImmutableArray<ModuleDefinition> Modules { get; }

        public override Utf8String? Name => Unified.Name;

        public MaterializedFieldEntity(TypeEntityMap map, UnifiedFieldEntity unified, ImmutableArray<ModuleDefinition> modules) : base(map) {
            Unified = unified;
            Modules = modules;
        }

        // true by-definition
        protected override bool GetHasUnifiableInitializer() => true;

        protected override FieldInitializer? GetInitializer() {
            throw new NotImplementedException();
        }

        protected override ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore() {
            throw new NotImplementedException();
        }

        protected override EntityBase GetUnifiedCore() => Unified;

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules() => Modules;
    }
}
