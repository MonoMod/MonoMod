using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonoMod.SourceGen.Internal.Extensions;
using MonoMod.SourceGen.Internal.Helpers;
using System.Collections.Immutable;
using System.Text;

namespace MonoMod.SourceGen.Internal.Interop {
    [Generator]
    public class FatInterfaceGenerator : IIncrementalGenerator {
        private const string FatInterfaceAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceAttribute";
        private const string FatInterfaceImplAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceImplAttribute";
        private const string FatInterfaceIgnoreAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceIgnoreAttribute";

        private sealed record TypeRef(string MdName, string FqName, string Refness);
        private static TypeRef CreateRef(ITypeSymbol symbol, string refness = "") {
            return new(symbol.GetFullyQualifiedMetadataName(), refness + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), refness);
        }
        private static TypeRef CreateRef(IParameterSymbol symbol) {
            return CreateRef(symbol.Type, GetRefString(symbol));
        }

        private sealed record FatIfaceMethod(string Name, TypeRef RetType, EquatableArray<TypeRef> Parameters, string Access);
        private sealed record FatInterfaceGenInfo(string MdName, EquatableArray<FatIfaceMethod> Methods);
        private sealed record FatIfaceImplInfo(string ImplType, TypeRef IfaceType);

        private static ImmutableArray<FatIfaceMethod> GetIfaceTypeMethods(INamedTypeSymbol sym) {
            using var methodListBuilder = ImmutableArrayBuilder<FatIfaceMethod>.Rent();
            foreach (var member in sym.GetMembers()) {
                if (member is not IMethodSymbol method) {
                    continue;
                }
                if (method.IsStatic || !method.IsPartialDefinition) {
                    continue;
                }
                if (method.HasAttributeWithFullyQualifiedMetadataName(FatInterfaceIgnoreAttribute)) {
                    continue;
                }

                using var paramsBuilder = ImmutableArrayBuilder<TypeRef>.Rent();
                foreach (var param in method.Parameters) {
                    paramsBuilder.Add(CreateRef(param));
                }

                methodListBuilder.Add(new(method.Name, CreateRef(method.ReturnType), paramsBuilder.ToImmutable(), GetAccessibililty(method.DeclaredAccessibility)));
            }
            return methodListBuilder.ToImmutable();
        }

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            // find our methods to implement
            var interfaces = context.SyntaxProvider.ForAttributeWithMetadataName(FatInterfaceAttribute,
                (n, ct) => n is StructDeclarationSyntax,
                (ctx, ct) => {
                    var sym = (INamedTypeSymbol) ctx.TargetSymbol;
                    var methods = GetIfaceTypeMethods(sym);
                    return new FatInterfaceGenInfo(sym.GetFullyQualifiedMetadataName(), methods);
                });

            var interfaceImpls = context.SyntaxProvider.ForAttributeWithMetadataName(FatInterfaceImplAttribute,
                (n, ct) => n is StructDeclarationSyntax,
                (ctx, ct) => {
                    if (ctx.Attributes is not [AttributeData { ConstructorArguments: [{ Value: INamedTypeSymbol ifaceType }] }]) {
                        return null;
                    }

                    return new FatIfaceImplInfo(((INamedTypeSymbol) ctx.TargetSymbol).GetFullyQualifiedMetadataName(), CreateRef(ifaceType));
                }).Where(x => x is not null);

            // then actually generate the code

            // first, the interface decls
            context.RegisterImplementationSourceOutput(interfaces.Combine(context.CompilationProvider), GenerateIfaceDecl);
            context.RegisterImplementationSourceOutput(interfaceImpls.Combine(context.CompilationProvider), GenerateIfaceImpl!);
        }

        const string IntPtr = "global::System.IntPtr";

        private static string GetRefString(IParameterSymbol param)
            => param.RefKind switch {
                RefKind.None => "",
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => "/*unknown ref kind*/ ",
            };

        private static string GetAccessibililty(Accessibility acc)
            => acc switch {
                Accessibility.NotApplicable => "",
                Accessibility.Private => "private ",
                Accessibility.ProtectedAndInternal => "private protected ",
                Accessibility.Protected => "protected ",
                Accessibility.Internal => "internal ",
                Accessibility.ProtectedOrInternal => "protected internal ",
                Accessibility.Public => "public ",
                _ => "/*unknown accessibility*/ ",
            };

        private void GenerateIfaceDecl(SourceProductionContext ctx, (FatInterfaceGenInfo info, Compilation compilation) tup) {
            // TODO: pool stringbuilder/codebuilder
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);
            cb.WriteHeader();
            var fname = DoGenerateIfaceDecl(cb, tup.compilation, tup.info);
            ctx.AddSource(fname + ".g.cs", sb.ToString());
        }

        private static string DoGenerateIfaceDecl(CodeBuilder code, Compilation compilation, FatInterfaceGenInfo info) {
            var ifType = compilation.Assembly.GetTypeByMetadataName(info.MdName);
            if (ifType is null) {
                code.WriteLine($"#error Could not get type with metadata name {info.MdName}");
                return $"fat_iface_unknown_{info.GetHashCode()}";
            }

            var typeCtx = new TypeSourceContext(ifType);
            typeCtx.AppendEnterContext(code, "unsafe");

            // common core code
            code.WriteLine("private readonly void* ptr_;")
                .WriteLine($"private readonly {IntPtr}[] vtbl_;")
                .WriteLine()
                .WriteLine($"public {ifType.Name}(void* ptr, {IntPtr}[] vtbl) {{")
                .IncreaseIndent().WriteLine("ptr_ = ptr; vtbl_ = vtbl;").DecreaseIndent()
                .WriteLine("}")
                .WriteLine();

            // the methods
            foreach (var method in info.Methods) {
                code.WriteLine($"{method.Access}partial {method.RetType.FqName} {method.Name}(")
                    .IncreaseIndent();

                var i = 0;
                foreach (var param in method.Parameters) {
                    if (i is not 0)
                        code.WriteLine(", ");

                    code.Write(param.FqName)
                        .Write(" _")
                        .Write(i.ToString("x2", null));
                    i++;
                }

                code.WriteLine().DecreaseIndent().WriteLine(") {").IncreaseIndent();
                code.Write("return ((delegate*<void*").IncreaseIndent();

                foreach (var param in method.Parameters) {
                    code.WriteLine(", ")
                        .Write(param.FqName);
                }

                code.WriteLine(", ")
                    .WriteLine(method.RetType.FqName)
                    .DecreaseIndent()
                    .Write(">) ")
                    .Write($"vtbl_[{i}])")
                    .Write("(ptr_")
                    .IncreaseIndent();

                i = 0;
                foreach (var param in method.Parameters) {
                    code.WriteLine(", ")
                        .Write(param.Refness)
                        .Write("_")
                        .Write(i.ToString("x2", null));
                    i++;
                }

                code.WriteLine()
                    .DecreaseIndent()
                    .WriteLine(");")
                    .DecreaseIndent()
                    .WriteLine("}")
                    .WriteLine();
            }

            typeCtx.AppendExitContext(code);
            return "FatIf_" + typeCtx.FullContextName;
        }

        private static void GenerateIfaceImpl(SourceProductionContext ctx, (FatIfaceImplInfo info, Compilation compilation) tup) {
            // TODO: pool stringbuilder/codebuilder
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);
            cb.WriteHeader();
            var fname = DoGenerateIfaceImpl(cb, tup.compilation, tup.info);
            ctx.AddSource(fname + ".g.cs", sb.ToString());
        }

        private static string DoGenerateIfaceImpl(CodeBuilder code, Compilation compilation, FatIfaceImplInfo info) {
            var implType = compilation.Assembly.GetTypeByMetadataName(info.ImplType);
            if (implType is null) {
                code.WriteLine($"#error Could not get type with metadata name {info.ImplType}");
                return $"fat_ifaceimpl_unknown_{info.GetHashCode()}";
            }

            var ifType = compilation.GetTypeByMetadataName(info.IfaceType.MdName);
            if (ifType is null) {
                code.WriteLine($"#error Could not get type with metadata name {info.IfaceType.MdName}");
                return $"fat_ifaceimpl_unknown_{info.GetHashCode()}";
            }

            var typeCtx = new TypeSourceContext(implType);

            if (!ifType.HasAttributeWithFullyQualifiedMetadataName(FatInterfaceAttribute)) {
                code.WriteLine($"#error Target type {ifType.GetFullyQualifiedName()} is not a fat interface");
                return "FatIfImpl_" + typeCtx.FullContextName;
            }

            var ifaceMethods = GetIfaceTypeMethods(ifType);
            typeCtx.AppendEnterContext(code, "unsafe");

            code.WriteLine($"private static {IntPtr}[]? fatVtable_;")
                .WriteLine($"public static {IntPtr}[] FatVtable_ {{ get {{").IncreaseIndent();

            var i = 0;
            foreach (var method in ifaceMethods) {
                code.Write($"static {method.RetType.FqName} S_{method.Name}_{i}(void* ptr__")
                    .IncreaseIndent();
                var j = 0;
                foreach (var param in method.Parameters) {
                    code.WriteLine(", ")
                        .Write(param.FqName)
                        .Write(' ')
                        .Write(j.ToString("x2", null));
                    j++;
                }

                code.WriteLine().DecreaseIndent().WriteLine(") {").IncreaseIndent()
                    .Write($"return (({implType.Name}*)ptr__)->{method.Name}(").IncreaseIndent();

                j = 0;
                foreach (var param in method.Parameters) {
                    if (j is not 0)
                        code.WriteLine(", ");

                    code.Write(param.Refness)
                        .Write('_')
                        .Write(j.ToString("x2", null));
                    j++;
                }

                code.WriteLine()
                    .DecreaseIndent().WriteLine(");")
                    .DecreaseIndent().WriteLine("}");

                i++;
            }

            code.WriteLine($"return fatVtable_ ??= new {IntPtr}[] {{").IncreaseIndent();

            i = 0;
            foreach (var method in ifaceMethods) {
                code.Write($"({IntPtr}) (delegate*<void*").IncreaseIndent();

                foreach (var param in method.Parameters) {
                    code.WriteLine(", ")
                        .Write(param.FqName);
                }

                code.WriteLine(", ");
                code.WriteLine(method.RetType.FqName)
                    .DecreaseIndent()
                    .WriteLine($">) &S_{method.Name}_{i}, ");
                i++;
            }

            code.DecreaseIndent().WriteLine("};");

            code.DecreaseIndent().WriteLine("} }");

            typeCtx.AppendExitContext(code);
            return "FatIfImpl_" + typeCtx.FullContextName;
        }
    }
}
