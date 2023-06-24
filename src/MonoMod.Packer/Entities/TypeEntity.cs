using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Diagnostics;
using MonoMod.Packer.Utilities;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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
            return Map.GetTypeMergeMode(Definition);
        }

        protected override TypeEntityBase? GetBaseType() {
            if (Definition.BaseType is { } @base) {
                if (Definition.IsTypeOf("System", "Object")) {
                    Map.Diagnostics.ReportDiagnostic(ErrorCode.ERR_SystemObjectDefinitionHasBase, Definition);
                }
                return Map.GetEntity(@base);
            } else {
                return null;
            }
        }

        protected override bool GetHasUnifiableBase() => true;

        private ConstructorScanner? lazyCtorScanner;
        public ConstructorScanner CtorScanner {
            get {
                if (lazyCtorScanner is null) {
                    Interlocked.CompareExchange(
                        ref lazyCtorScanner,
                        new(Map, Definition),
                        null
                    );
                }

                return lazyCtorScanner;
            }
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
