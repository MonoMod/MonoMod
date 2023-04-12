using Microsoft.CodeAnalysis;
using MonoMod.SourceGen.Internal.Extensions;
using MonoMod.SourceGen.Internal.Helpers;

namespace MonoMod.SourceGen.Internal {
    internal sealed record TypeRef(string MdName, string FqName, string Name, string Refness);

    internal sealed record TypeContext(string? Namespace, TypeRef InnermostType,
        string FullContextName, EquatableArray<string> ContainingTypeDecls) {
        public void AppendEnterContext(CodeBuilder builder, string additionalModifiers = "") {
            if (Namespace is not null) {
                builder.Write("namespace ").WriteLine(Namespace).OpenBlock();
            }
            for (var i = ContainingTypeDecls.AsImmutableArray().Length - 1; i >= 0 ; i--) {
                if (!string.IsNullOrEmpty(additionalModifiers)) {
                    _ = builder
                        .Write(additionalModifiers)
                        .Write(' ');
                }
                _ = builder
                    .WriteLine(ContainingTypeDecls[i])
                    .OpenBlock();
            }
        }
        public void AppendExitContext(CodeBuilder builder) {
            for (var i = 0; i < ContainingTypeDecls.AsImmutableArray().Length; i++) {
                _ = builder.CloseBlock();
            }
            if (Namespace is not null) {
                _ = builder.CloseBlock();
            }
        }
    }

    internal static class GenHelpers {

        public static TypeRef CreateRef(ITypeSymbol symbol, string refness = "") {
            return new(symbol.GetFullyQualifiedMetadataName(), refness + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), symbol.Name, refness);
        }

        public static TypeRef CreateRef(IParameterSymbol symbol) {
            return CreateRef(symbol.Type, GetRefString(symbol));
        }

        public static string GetRefString(IParameterSymbol param)
            => param.RefKind switch {
                RefKind.None => "",
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => "/*unknown ref kind*/ ",
            };

        public static TypeContext CreateTypeContext(INamedTypeSymbol type) {
            var innermostType = type;

            using var builder = ImmutableArrayBuilder<string>.Rent();
            INamedTypeSymbol? outerType = null;
            while (innermostType is not null) {
                outerType = innermostType;

                var isRec = innermostType.IsRecord;
                var isStruct = innermostType.IsValueType;
                var isRef = innermostType.IsReferenceType;
                builder.Add($"partial {(isRec ? "record" : "")}{(isRef && !isRec ? "class" : "")} {(isStruct ? "struct" : "")} {innermostType.Name}");

                innermostType = innermostType.ContainingType;
            }

            var ns = outerType?.ContainingNamespace?.ToDisplayString();

            var typeCtx = "";
            innermostType = type;
            while (innermostType is not null) {
                typeCtx = innermostType.Name + typeCtx; 
                innermostType = innermostType.ContainingType;
                if (innermostType is not null)
                    typeCtx = "." + typeCtx;
            }
            typeCtx = ns + (ns is not null ? "." : "") + typeCtx;

            var decls = builder.ToImmutable();
            return new(ns, CreateRef(type), typeCtx, decls);
        }
    }
}
