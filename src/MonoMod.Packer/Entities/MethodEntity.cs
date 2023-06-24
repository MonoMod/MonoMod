using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class MethodEntity : MethodEntityBase {
        private string DebuggerDisplay() => Definition.ToString();

        public readonly MethodDefinition Definition;

        public MethodEntity(TypeEntityMap map, MethodDefinition def) : base(map) {
            Definition = def;
        }

        public override Utf8String? Name => Definition.Name;

        public TypeEntity DeclaringType => Map.Lookup(Definition.DeclaringType!);

        public new ImmutableArray<TypeEntity> TypesInSignature => base.TypesInSignature.CastArray<TypeEntity>();
        protected override ImmutableArray<TypeEntityBase> MakeTypesInSignatureCore() {
            if (Definition.Signature is { } sig) {
                return Map
                    .RentTypeInSigBuilder()
                    .Visit(sig)
                    .ToImmutableAndReturn()
                    .CastArray<TypeEntityBase>();
            } else {
                return ImmutableArray<TypeEntity>.Empty.CastArray<TypeEntityBase>();
            }
        }

        protected override ImmutableArray<ModuleDefinition> MakeContributingModules()
            => Definition.Module is { } module
                ? ImmutableArray.Create(module)
                : ImmutableArray<ModuleDefinition>.Empty;

        private UnifiedMethodEntity? lazyUnified;
        public UnifiedMethodEntity GetUnified() {
            if (Volatile.Read(ref lazyUnified) is { } result)
                return result;
            // this is SLOW
            var unifiedDeclType = DeclaringType.UnifiedType;
            var methodSet = Definition.IsStatic
                ? unifiedDeclType.StaticMethods
                : unifiedDeclType.InstanceMethods;
            var fullSig = Definition.Signature?.ToString();
            return lazyUnified ??= methodSet.First(m => m.Name == Name && m.FullSig == fullSig);
        }

        protected override EntityBase GetUnifiedCore() => GetUnified();

    }
}
