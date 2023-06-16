﻿using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Immutable;
using System.Diagnostics;

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
    }
}
