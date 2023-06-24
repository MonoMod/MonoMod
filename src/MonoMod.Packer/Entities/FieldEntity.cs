using AsmResolver;
using AsmResolver.DotNet;
using MonoMod.Packer.Utilities;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MonoMod.Packer.Entities {
    [DebuggerDisplay($"{{{nameof(DebuggerDisplay)}(),nq}}")]
    internal sealed class FieldEntity : FieldEntityBase {
        private string DebuggerDisplay() => Definition.ToString();

        public readonly FieldDefinition Definition;

        public FieldEntity(TypeEntityMap map, FieldDefinition def) : base(map) {
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

        private UnifiedFieldEntity? lazyUnified;
        public UnifiedFieldEntity GetUnified() {
            if (Volatile.Read(ref lazyUnified) is { } result)
                return result;
            // this is SLOW
            var unifiedDeclType = DeclaringType.UnifiedType;
            var fieldSet = Definition.IsStatic
                ? unifiedDeclType.StaticFields
                : unifiedDeclType.InstanceFields;
            return lazyUnified ??= fieldSet.First(f => f.Name == Name);
        }

        protected override bool GetHasUnifiableInitializer() {
            var scanner = DeclaringType.CtorScanner;
            var hasInit = scanner.TryGetInitializer(Definition, out var init);
            return !hasInit || init is not null; // if hasInit && init is null, the initializer is non-unifiable
        }

        protected override FieldInitializer? GetInitializer() {
            var scanner = DeclaringType.CtorScanner;
            return scanner.TryGetInitializer(Definition, out var init) ? init : null;
        }
    }
}