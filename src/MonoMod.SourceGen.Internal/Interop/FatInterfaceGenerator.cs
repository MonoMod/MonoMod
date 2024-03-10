using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonoMod.SourceGen.Internal.Extensions;
using MonoMod.SourceGen.Internal.Helpers;
using System.Collections.Immutable;
using System.Text;

namespace MonoMod.SourceGen.Internal.Interop
{
    [Generator]
    public class FatInterfaceGenerator : IIncrementalGenerator
    {
        private const string FatInterfaceAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceAttribute";
        private const string FatInterfaceImplAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceImplAttribute";
        private const string FatInterfaceIgnoreAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceIgnoreAttribute";


        private sealed record FatIfaceMethod(string Name, TypeRef RetType, EquatableArray<TypeRef> Parameters, string Access);
        private sealed record FatInterfaceGenInfo(TypeContext Type, EquatableArray<FatIfaceMethod> Methods);
        private sealed record FatIfaceImplInfo(TypeContext Type, TypeRef IfaceType, bool IfaceHasAttr, EquatableArray<FatIfaceMethod> IfaceMethods);

        private static ImmutableArray<FatIfaceMethod> GetIfaceTypeMethods(INamedTypeSymbol sym)
        {
            using var methodListBuilder = ImmutableArrayBuilder<FatIfaceMethod>.Rent();
            foreach (var member in sym.GetMembers())
            {
                if (member is not IMethodSymbol method)
                {
                    continue;
                }
                if (method.IsStatic || !method.IsPartialDefinition)
                {
                    continue;
                }
                if (method.HasAttributeWithFullyQualifiedMetadataName(FatInterfaceIgnoreAttribute))
                {
                    continue;
                }

                using var paramsBuilder = ImmutableArrayBuilder<TypeRef>.Rent();
                foreach (var param in method.Parameters)
                {
                    paramsBuilder.Add(GenHelpers.CreateRef(param));
                }

                methodListBuilder.Add(new(method.Name, GenHelpers.CreateRef(method.ReturnType), paramsBuilder.ToImmutable(), GetAccessibililty(method.DeclaredAccessibility)));
            }
            return methodListBuilder.ToImmutable();
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // find our methods to implement
            var interfaces = context.SyntaxProvider.ForAttributeWithMetadataName(FatInterfaceAttribute,
                (n, ct) => n is StructDeclarationSyntax,
                (ctx, ct) =>
                {
                    var sym = (INamedTypeSymbol)ctx.TargetSymbol;
                    var methods = GetIfaceTypeMethods(sym);
                    return new FatInterfaceGenInfo(GenHelpers.CreateTypeContext(sym), methods);
                });

            var interfaceImpls = context.SyntaxProvider.ForAttributeWithMetadataName(FatInterfaceImplAttribute,
                (n, ct) => n is StructDeclarationSyntax,
                (ctx, ct) =>
                {
                    if (ctx.Attributes is not [AttributeData { ConstructorArguments: [{ Value: INamedTypeSymbol ifaceType }] }])
                    {
                        return null;
                    }

                    return new FatIfaceImplInfo(GenHelpers.CreateTypeContext((INamedTypeSymbol)ctx.TargetSymbol),
                        GenHelpers.CreateRef(ifaceType),
                        ifaceType.HasAttributeWithFullyQualifiedMetadataName(FatInterfaceAttribute),
                        GetIfaceTypeMethods(ifaceType));
                }).Where(x => x is not null);

            // then actually generate the code

            // first, the interface decls
            context.RegisterImplementationSourceOutput(interfaces, GenerateIfaceDecl);
            context.RegisterImplementationSourceOutput(interfaceImpls, GenerateIfaceImpl!);
        }

        const string IntPtr = "global::System.IntPtr";

        private static string GetAccessibililty(Accessibility acc)
            => acc switch
            {
                Accessibility.NotApplicable => "",
                Accessibility.Private => "private ",
                Accessibility.ProtectedAndInternal => "private protected ",
                Accessibility.Protected => "protected ",
                Accessibility.Internal => "internal ",
                Accessibility.ProtectedOrInternal => "protected internal ",
                Accessibility.Public => "public ",
                _ => "/*unknown accessibility*/ ",
            };

        private void GenerateIfaceDecl(SourceProductionContext ctx, FatInterfaceGenInfo info)
        {
            // TODO: pool stringbuilder/codebuilder
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);
            cb.WriteHeader();
            var fname = DoGenerateIfaceDecl(cb, info);
            ctx.AddSource(fname + ".g.cs", sb.ToString());
        }

        private static string DoGenerateIfaceDecl(CodeBuilder code, FatInterfaceGenInfo info)
        {
            info.Type.AppendEnterContext(code, "unsafe");

            // common core code
            code.WriteLine("private readonly void* ptr_;")
                .WriteLine($"private readonly {IntPtr}[] vtbl_;")
                .WriteLine()
                .WriteLine($"public {info.Type.InnermostType.Name}(void* ptr, {IntPtr}[] vtbl) {{")
                .IncreaseIndent().WriteLine("ptr_ = ptr; vtbl_ = vtbl;").DecreaseIndent()
                .WriteLine("}")
                .WriteLine();

            // the methods
            foreach (var method in info.Methods)
            {
                code.WriteLine($"{method.Access}partial {method.RetType.FqName} {method.Name}(")
                    .IncreaseIndent();

                var i = 0;
                foreach (var param in method.Parameters)
                {
                    if (i is not 0)
                        code.WriteLine(", ");

                    code.Write(param.FqName)
                        .Write(" _")
                        .Write(i.ToString("x2", null));
                    i++;
                }

                code.WriteLine().DecreaseIndent().WriteLine(") {").IncreaseIndent();
                code.Write("return ((delegate*<void*").IncreaseIndent();

                foreach (var param in method.Parameters)
                {
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
                foreach (var param in method.Parameters)
                {
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

            info.Type.AppendExitContext(code);
            return "FatIf_" + info.Type.FullContextName;
        }

        private static void GenerateIfaceImpl(SourceProductionContext ctx, FatIfaceImplInfo info)
        {
            // TODO: pool stringbuilder/codebuilder
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);
            cb.WriteHeader();
            var fname = DoGenerateIfaceImpl(cb, info);
            ctx.AddSource(fname + ".g.cs", sb.ToString());
        }

        private static string DoGenerateIfaceImpl(CodeBuilder code, FatIfaceImplInfo info)
        {
            if (!info.IfaceHasAttr)
            {
                code.WriteLine($"#error Target type {info.IfaceType.FqName} is not a fat interface");
                return "FatIfImpl_" + info.Type.FullContextName;
            }

            var ifaceMethods = info.IfaceMethods;
            info.Type.AppendEnterContext(code, "unsafe");

            code.WriteLine($"private static {IntPtr}[]? fatVtable_;")
                .WriteLine($"public static {IntPtr}[] FatVtable_ {{ get {{").IncreaseIndent();

            var i = 0;
            foreach (var method in ifaceMethods)
            {
                code.Write($"static {method.RetType.FqName} S_{method.Name}_{i}(void* ptr__")
                    .IncreaseIndent();
                var j = 0;
                foreach (var param in method.Parameters)
                {
                    code.WriteLine(", ")
                        .Write(param.FqName)
                        .Write(' ')
                        .Write(j.ToString("x2", null));
                    j++;
                }

                code.WriteLine().DecreaseIndent().WriteLine(") {").IncreaseIndent()
                    .Write($"return (({info.Type.InnermostType.FqName}*)ptr__)->{method.Name}(").IncreaseIndent();

                j = 0;
                foreach (var param in method.Parameters)
                {
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
            foreach (var method in ifaceMethods)
            {
                code.Write($"({IntPtr}) (delegate*<void*").IncreaseIndent();

                foreach (var param in method.Parameters)
                {
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

            info.Type.AppendExitContext(code);
            return "FatIfImpl_" + info.Type.FullContextName;
        }
    }
}
