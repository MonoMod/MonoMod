using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using MonoMod.Packer.Entities;
using System.Collections.Immutable;
using MethodSignature = AsmResolver.DotNet.Signatures.MethodSignature;

namespace MonoMod.Packer.Utilities {
    internal sealed class TypesInSignatureBuilder : ITypeSignatureVisitor<TypesInSignatureBuilder.VoidStruct> {
        private readonly struct VoidStruct { }

        private readonly TypeEntityMap map;
        private readonly ImmutableArray<TypeEntity>.Builder builder = ImmutableArray.CreateBuilder<TypeEntity>();

        public TypesInSignatureBuilder(TypeEntityMap map) {
            this.map = map;
        }

        public void Clear() {
            builder.Clear();
        }

        public ImmutableArray<TypeEntity> ToImmutable() => builder.ToImmutable();
        public ImmutableArray<TypeEntity> ToImmutableAndReturn() {
            var result = ToImmutable();
            map.ReturnTypeInSigBuilder(this);
            return result;
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitArrayType(ArrayTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitBoxedType(BoxedTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitByReferenceType(ByReferenceTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitCorLibType(CorLibTypeSignature signature) {
            if (!map.Options.ExcludeCorelib) {
                // if we're excluding corlib, then we don't need to include corlib types here
                VisitTypeDefOrRef(signature.Type);
            }
            return default;
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitCustomModifierType(CustomModifierTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitGenericInstanceType(GenericInstanceTypeSignature signature) {
            VisitTypeDefOrRef(signature.GenericType);
            foreach (var arg in signature.TypeArguments) {
                _ = arg.AcceptVisitor(this);
            }
            return default;
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitGenericParameter(GenericParameterSignature signature) {
            // no-op
            return default;
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitPinnedType(PinnedTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitPointerType(PointerTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitSentinelType(SentinelTypeSignature signature) {
            // no-op
            return default;
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitSzArrayType(SzArrayTypeSignature signature) {
            return signature.BaseType.AcceptVisitor(this);
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitTypeDefOrRef(TypeDefOrRefSignature signature) {
            VisitTypeDefOrRef(signature.Type);
            return default;
        }

        VoidStruct ITypeSignatureVisitor<VoidStruct>.VisitFunctionPointerType(FunctionPointerTypeSignature signature) {
            Visit(signature.Signature);
            return default;
        }

        public TypesInSignatureBuilder Visit(MethodSignature sig) {
            sig.ReturnType.AcceptVisitor(this);
            foreach (var param in sig.ParameterTypes) {
                param.AcceptVisitor(this);
            }
            return this;
        }

        public TypesInSignatureBuilder Visit(FieldSignature sig) {
            sig.FieldType.AcceptVisitor(this);
            return this;
        }

        public TypesInSignatureBuilder Visit(TypeSignature sig) {
            sig.AcceptVisitor(this);
            return this;
        }

        private void VisitTypeDefOrRef(ITypeDefOrRef defOrRef) {
            var resolved = map.MdResolver.ResolveType(defOrRef);
            if (resolved is not null) {
                builder.Add(map.Lookup(resolved));
            }
        }
    }
}
