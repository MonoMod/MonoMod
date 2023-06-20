using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class ExternalTypeEntity : TypeEntityBase {
        private string DebuggerDisplay() => $"External {Reference.FullName}";

        public readonly ITypeDefOrRef Reference;
        private TypeDefinition? lazyDefinition;

        public TypeDefinition? Definition {
            get {
                if (lazyDefinition is null) {
                    // note: we just call Resolve() here, because this should only exist for types external to the merge, so Map's mdresolver is wrong
                    Interlocked.CompareExchange(ref lazyDefinition, Map.ExternalMdResolver.ResolveType(Reference), null);
                }
                return lazyDefinition;
            }
        }

        public ExternalTypeEntity(TypeEntityMap map, ITypeDefOrRef reference) : base(map) {
            Reference = reference;
        }

        public override Utf8String? Namespace => Reference.Namespace;

        public override Utf8String? Name => Reference.Name;

        protected override TypeEntityBase? GetBaseType() {
            var baseRef = Definition?.BaseType;
            if (baseRef is not null) {
                return Map.GetEntity(baseRef);
            } else {
                return null;
            }
        }

        protected override TypeMergeMode? GetTypeMergeMode() {
            return TypeMergeMode.DoNotMerge;
        }

        protected override bool GetHasUnifiableBase() {
            // this really shouldn't be called for this type
            throw new System.NotImplementedException();
        }

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules() {
            if (Definition?.Module is { } module) {
                return ImmutableArray.Create(module);
            } else {
                return ImmutableArray<ModuleDefinition>.Empty;
            }
        }

        // TODO: members? or do those matter?
        protected override ImmutableArray<FieldEntityBase> MakeInstanceFields() {
            return ImmutableArray<FieldEntityBase>.Empty;
        }

        protected override ImmutableArray<MethodEntityBase> MakeInstanceMethods() {
            return ImmutableArray<MethodEntityBase>.Empty;
        }

        protected override ImmutableArray<TypeEntityBase> MakeNestedTypes() {
            return ImmutableArray<TypeEntityBase>.Empty;
        }

        protected override ImmutableArray<FieldEntityBase> MakeStaticFields() {
            return ImmutableArray<FieldEntityBase>.Empty;
        }

        protected override ImmutableArray<MethodEntityBase> MakeStaticMethods() {
            return ImmutableArray<MethodEntityBase>.Empty;
        }
    }
}
