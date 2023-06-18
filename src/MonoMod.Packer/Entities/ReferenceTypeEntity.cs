using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Immutable;
using System.Diagnostics;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class ReferenceTypeEntity : TypeEntityBase {
        private string DebuggerDisplay() => Reference.FullName;

        public readonly ITypeDefOrRef Reference;

        public ReferenceTypeEntity(TypeEntityMap map, ITypeDefOrRef reference) : base(map) {
            Reference = reference;
        }

        public override Utf8String? Namespace => Reference.Namespace;

        public override Utf8String? Name => Reference.Name;

        protected override TypeEntityBase? GetBaseType() {
            // this is an externally referenced type; we don't care
            return null;
        }

        protected override TypeMergeMode? GetTypeMergeMode() {
            return TypeMergeMode.DoNotMerge;
        }

        protected override bool GetHasUnifiableBase() {
            // this really shouldn't be called for this type
            throw new System.NotImplementedException();
        }

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules() {
            return ImmutableArray<ModuleDefinition>.Empty;
        }

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
