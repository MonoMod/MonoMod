using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Utilities;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class TypeEntity : TypeEntityBase {
        private string DebuggerDisplay() => Definition.FullName;

        public readonly TypeDefinition Definition;

        public TypeEntity(TypeEntityMap map, TypeDefinition def) : base(map) {
            Definition = def;
        }

        public override Utf8String? Namespace => Definition.Namespace;
        public override Utf8String? Name => Definition.Name;

        private UnifiedTypeEntity? lazyUnifiedType;
        public UnifiedTypeEntity UnifiedType => lazyUnifiedType ??= GetUnifiedType();

        private UnifiedTypeEntity GetUnifiedType() {
            if (Definition.DeclaringType is null) {
                // we can just look up by name
                return Map.ByName(Namespace, Name);
            } else {
                return Map
                    .Lookup(Definition.DeclaringType)
                    .GetUnifiedType()
                    .NestedTypes
                    .Single(t => t.Name == Name);
            }
        }

        protected override TypeMergeMode? GetTypeMergeMode() {
            return Definition.GetDeclaredMergeMode();
        }

        protected override bool GetHasBase() {
            return Definition.BaseType is not null;
        }
        protected override bool GetHasUnifiableBase() {
            return Definition.BaseType is not { } @base // has no base type
                || Map.MdResolver.ResolveType(@base) is not { } type // or that type is not in our resolve set
                || Map.TryLookup(type, out _); // or lookup of that type succeeded; this *should* always pass
        }

        public new TypeEntity? UnifiableBase => (TypeEntity?)base.UnifiableBase;
        protected override TypeEntityBase? GetUnifiableBase() {
            var @base = Definition.BaseType;
            if (@base is null)
                return null;
            var resolved = Map.MdResolver.ResolveType(@base);
            if (resolved is null)
                return null;
            return Map.TryLookup(resolved, out var result) ? result : null;
        }

        public override bool IsModuleType => Definition.IsModuleType;

        private MethodEntity CreateMethod(MethodDefinition m) => new(Map, m);
        private FieldEntity CreateField(FieldDefinition f) => new(Map, f);

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules()
            => Definition.Module is { } module
                ? ImmutableArray.Create(module)
                : ImmutableArray<ModuleDefinition>.Empty;

        public new ImmutableArray<MethodEntity> StaticMethods => base.StaticMethods.CastArray<MethodEntity>();
        protected override ImmutableArray<MethodEntityBase> MakeStaticMethods() {
            return Definition.Methods
                        .Where(m => m.IsStatic)
                        .Select(CreateMethod)
                        .ToImmutableArray()
                        .CastArray<MethodEntityBase>();
        }

        public new ImmutableArray<MethodEntity> InstanceMethods => base.InstanceMethods.CastArray<MethodEntity>();
        protected override ImmutableArray<MethodEntityBase> MakeInstanceMethods() {
            return Definition.Methods
                            .Where(m => !m.IsStatic)
                            .Select(CreateMethod)
                            .ToImmutableArray()
                            .CastArray<MethodEntityBase>();
        }

        public new ImmutableArray<FieldEntity> StaticFields => base.StaticFields.CastArray<FieldEntity>();
        protected override ImmutableArray<FieldEntityBase> MakeStaticFields() {
            return Definition.Fields
                            .Where(f => f.IsStatic)
                            .Select(CreateField)
                            .ToImmutableArray()
                            .CastArray<FieldEntityBase>();
        }

        public new ImmutableArray<FieldEntity> InstanceFields => base.InstanceFields.CastArray<FieldEntity>();
        protected override ImmutableArray<FieldEntityBase> MakeInstanceFields() {
            return Definition.Fields
                            .Where(f => !f.IsStatic)
                            .Select(CreateField)
                            .ToImmutableArray()
                            .CastArray<FieldEntityBase>();
        }

        public new ImmutableArray<TypeEntity> NestedTypes => base.NestedTypes.CastArray<TypeEntity>();
        protected override ImmutableArray<TypeEntityBase> MakeNestedTypes() {
            return Definition.NestedTypes
                            .Select(Map.Lookup)
                            .ToImmutableArray()
                            .CastArray<TypeEntityBase>();
        }
    }
}
