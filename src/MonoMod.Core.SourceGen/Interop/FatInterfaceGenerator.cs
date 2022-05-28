using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoMod.Core.SourceGen.Interop {
    [Generator]
    public class FatInterfaceGenerator : ISourceGenerator {
        private const string FatInterfaceAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceAttribute";
        private const string FatInterfaceImplAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceImplAttribute";
        private const string FatInterfaceIgnoreAttribute = "MonoMod.Core.Interop.Attributes.FatInterfaceIgnoreAttribute";

        internal record FatInterfaceInfo(StructDeclarationSyntax TypeDef) {
            public List<MethodDeclarationSyntax> InterfaceMethods { get; } = new();
        }

        internal record FatInterfaceImplInfo(StructDeclarationSyntax TypeDef, TypeSyntax Interface);

        public void Initialize(GeneratorInitializationContext context) {
            context.RegisterForSyntaxNotifications(() => new SyntaxContextReciever());
        }

        internal class SyntaxContextReciever : ISyntaxContextReceiver {
            internal readonly List<FatInterfaceInfo> FatInterfaces = new();
            internal readonly List<FatInterfaceImplInfo> InterfaceImpls = new();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context) {
                if (context.Node is not StructDeclarationSyntax type) {
                    return;
                }

                var semantic = context.SemanticModel;

                var fatInterfaceAttr = GetAttribute(semantic, type.AttributeLists, FatInterfaceAttribute);
                if (fatInterfaceAttr is not null) {
                    VisitFatInterface(semantic, type);
                    return;
                }

                var fatInterfaceImplAttr = GetAttribute(semantic, type.AttributeLists, FatInterfaceImplAttribute);
                if (fatInterfaceImplAttr is not null) {
                    VisitFatInterfaceImpl(semantic, type, fatInterfaceImplAttr);
                    return;
                }
            }

            private void VisitFatInterface(SemanticModel semantic, StructDeclarationSyntax type) {
                var ifInfo = new FatInterfaceInfo(type);
                foreach (var member in type.Members) {
                    if (member is not MethodDeclarationSyntax method) {
                        continue;
                    }

                    if (!method.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)) {
                        continue;
                    }

                    if (method.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) {
                        continue;
                    }

                    var ignoreAttr = GetAttribute(semantic, method.AttributeLists, FatInterfaceIgnoreAttribute);
                    if (ignoreAttr is not null) {
                        continue;
                    }

                    ifInfo.InterfaceMethods.Add(method);
                }

                FatInterfaces.Add(ifInfo);
            }

            private void VisitFatInterfaceImpl(SemanticModel semantic, StructDeclarationSyntax type, AttributeSyntax fatInterfaceAttr) {
                var argList = fatInterfaceAttr.ArgumentList;
                if (argList is null) {
                    return;
                }

                if (argList.Arguments.Count < 1) {
                    // huh?
                    return;
                }

                var expr = argList.Arguments[0].Expression;
                if (expr is not TypeOfExpressionSyntax typeOf) {
                    return;
                }

                var implName = typeOf.Type;
                InterfaceImpls.Add(new(type, implName));
            }

            private static AttributeSyntax? GetAttribute(SemanticModel semantic, SyntaxList<AttributeListSyntax> attrLists, string attrType) {
                if (attrLists.Count < 0) {
                    return null;
                }

                foreach (var attrList in attrLists) {
                    foreach (var attr in attrList.Attributes) {
                        if (semantic.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrCtor) {
                            continue;
                        }

                        if (attrCtor.ContainingType is not { } type) {
                            // huh?
                            continue;
                        }

                        if (type.ToDisplayString() != attrType) {
                            continue;
                        }

                        return attr;
                    }
                }

                return null;
            }
        }

        public void Execute(GeneratorExecutionContext context) {
            if (context.SyntaxContextReceiver is not SyntaxContextReciever recv) {
                // huh?
                return;
            }

            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);

            foreach (var iface in recv.FatInterfaces) {
                GenerateSource(sb, cb, context, (x, b) => {
                    GenerateFatInterface(b, x.Compilation, iface, out var name);
                    return name;
                });
            }


            foreach (var impl in recv.InterfaceImpls) {
                GenerateSource(sb, cb, context, (x, b) => {
                    GenerateFatInterfaceImpl(b, x.Compilation, recv, impl, out var name);
                    return name;
                });
            }
        }

        private static void GenerateSource(StringBuilder sb, CodeBuilder cb, GeneratorExecutionContext ctx, Func<GeneratorExecutionContext, CodeBuilder, string> generate) {
            cb.WriteHeader();

            var name = generate(ctx, cb);

            ctx.AddSource($"{name}.g.cs", sb.ToString());
            sb.Clear();
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
                Accessibility.ProtectedAndInternal => "protected /*and*/ internal ",
                Accessibility.Protected => "protected ",
                Accessibility.Internal => "internal ",
                Accessibility.ProtectedOrInternal => "protected /*or*/ internal ",
                Accessibility.Public => "public ",
                _ => "/*unknown accessibility*/ ",
            };

        private static void GenerateFatInterface(CodeBuilder code, Compilation comp, FatInterfaceInfo iface, out string ifName) {
            var typeSem = comp.GetSemanticModel(iface.TypeDef.SyntaxTree);
            if (typeSem.GetDeclaredSymbol(iface.TypeDef) is not INamedTypeSymbol namedType) {
                ifName = $"fat_unknown_{iface.GetHashCode()}";
                code.WriteLine("// Could not get the declared symbol of the fat interface type");
                return;
            }

            var typeCtx = new TypeSourceContext(namedType);
            ifName = "FatIf_" + typeCtx.FullContextName;

            typeCtx.AppendEnterContext(code, "unsafe");

            // first, we want the core implementation, which is the same for all interfaces
            code.WriteLine("private readonly void* ptr_;")
                .WriteLine($"private readonly {IntPtr}[] vtbl_;")
                .WriteLine()
                .WriteLine($"public {namedType.Name}(void* ptr, {IntPtr}[] vtbl) {{")
                .IncreaseIndent().WriteLine("ptr_ = ptr; vtbl_ = vtbl;").DecreaseIndent()
                .WriteLine("}")
                .WriteLine();

            for (var i = 0; i < iface.InterfaceMethods.Count; i++) {
                var methSyntax = iface.InterfaceMethods[i];
                var semantic = comp.GetSemanticModel(methSyntax.SyntaxTree);
                if (semantic.GetDeclaredSymbol(methSyntax) is not IMethodSymbol method) {
                    code.WriteLine($"// Could not get symbol for method {methSyntax.Identifier.Text}");
                    continue;
                }

                var access = GetAccessibililty(method.DeclaredAccessibility);

                var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                code.WriteLine($"{access}partial {returnType} {method.Name}(")
                    .IncreaseIndent();

                var isFirst = true;
                foreach (var param in method.Parameters) {
                    if (isFirst)
                        code.WriteLine(", ");
                    isFirst = false;

                    var refKind = GetRefString(param);

                    code.Write(refKind)
                        .Write(param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .Write(' ')
                        .Write(param.Name);
                }

                code.WriteLine().DecreaseIndent().WriteLine(") {").IncreaseIndent();
                code.Write("return ((delegate*<void*").IncreaseIndent();

                foreach (var param in method.Parameters) {
                    code.WriteLine(", ");

                    var refKind = GetRefString(param);

                    code.Write(refKind)
                        .Write(param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                code.WriteLine(", ");
                code.WriteLine(returnType)
                    .DecreaseIndent()
                    .Write(">) ")
                    .Write($"vtbl_[{i}])")
                    .Write("(ptr_")
                    .IncreaseIndent();

                foreach (var param in method.Parameters) {
                    code.WriteLine(", ");

                    var refKind = GetRefString(param);

                    code.Write(refKind)
                        .Write(param.Name);
                }

                code.WriteLine()
                    .DecreaseIndent()
                    .WriteLine(");")
                    .DecreaseIndent()
                    .WriteLine("}")
                    .WriteLine();
            }

            typeCtx.AppendExitContext(code);
        }

        private static void GenerateFatInterfaceImpl(CodeBuilder code, Compilation comp, SyntaxContextReciever recv, FatInterfaceImplInfo ifaceImpl, out string ifName) {
            var implTypeSem = comp.GetSemanticModel(ifaceImpl.TypeDef.SyntaxTree);
            if (implTypeSem.GetDeclaredSymbol(ifaceImpl.TypeDef) is not INamedTypeSymbol implType) {
                ifName = $"unknown_impl_{ifaceImpl.GetHashCode()}";
                code.WriteLine("// Could not get implementation type symbol");
                return;
            }

            ifName = $"FatIfImpl_{implType.ToDisplayString()}";

            var ifaceTypeSem = comp.GetSemanticModel(ifaceImpl.Interface.SyntaxTree);
            if (ifaceTypeSem.GetSymbolInfo(ifaceImpl.Interface).Symbol is not INamedTypeSymbol ifaceType) {
                code.WriteLine("// Could not get interface symbol");
                return;
            }

            var typeCtx = new TypeSourceContext(implType);
            ifName = $"FatIfImpl_{typeCtx.FullContextName}";

            if (FindMatchingFatInterface(comp, recv, ifaceType) is not { } fatIface) {
                code.WriteLine($"// Could not find fat interface {ifaceType.ToDisplayString()}");
                return;
            }

            typeCtx.AppendEnterContext(code, "unsafe");

            code.WriteLine($"private static {IntPtr}[]? fatVtable_;");
            code.WriteLine($"public static {IntPtr}[] FatVtable_ {{ get {{").IncreaseIndent();

            var methSymbols = new List<(IMethodSymbol, int)>();
            foreach (var methSyntax in fatIface.InterfaceMethods) {
                var semantic = comp.GetSemanticModel(methSyntax.SyntaxTree);
                if (semantic.GetDeclaredSymbol(methSyntax) is not IMethodSymbol method) {
                    code.WriteLine($"// Could not get symbol for method {methSyntax.Identifier.Text}");
                    continue;
                }

                var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                code.WriteLine($"static {returnType} S_{method.Name}_{methSymbols.Count}(void* ptr__")
                    .IncreaseIndent();

                methSymbols.Add((method, methSymbols.Count));

                foreach (var param in method.Parameters) {
                    code.WriteLine(", ");

                    var refKind = GetRefString(param);

                    code.Write(refKind)
                        .Write(param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .Write(' ')
                        .Write(param.Name);
                }

                code.WriteLine().DecreaseIndent().WriteLine(") {").IncreaseIndent();

                code.Write($"return (({implType.Name}*)ptr__)->{method.Name}(").IncreaseIndent();

                var isFirst = true;
                foreach (var param in method.Parameters) {
                    if (isFirst)
                        code.WriteLine(", ");
                    isFirst = false;

                    var refKind = GetRefString(param);

                    code.Write(refKind)
                        .Write(param.Name);
                }

                code.WriteLine()
                    .DecreaseIndent().WriteLine(");")
                    .DecreaseIndent().WriteLine("}");

            }

            code.WriteLine($"return fatVtable_ ??= new {IntPtr}[] {{").IncreaseIndent();

            foreach (var (method, num) in methSymbols) {
                var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                code.Write($"({IntPtr}) (delegate*<void*").IncreaseIndent();

                foreach (var param in method.Parameters) {
                    code.WriteLine(", ");

                    var refKind = GetRefString(param);

                    code.Write(refKind)
                        .Write(param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                code.WriteLine(", ");
                code.WriteLine(returnType)
                    .DecreaseIndent()
                    .WriteLine($">) &S_{method.Name}_{num}, ");
            }

            code.DecreaseIndent().WriteLine("};");

            code.DecreaseIndent().WriteLine("} }");

            typeCtx.AppendExitContext(code);
        }

        private static FatInterfaceInfo? FindMatchingFatInterface(Compilation comp, SyntaxContextReciever recv, INamedTypeSymbol ifSymbol) {
            foreach (var iface in recv.FatInterfaces) {
                var model = comp.GetSemanticModel(iface.TypeDef.SyntaxTree);
                if (model.GetDeclaredSymbol(iface.TypeDef) is not INamedTypeSymbol cur) {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(ifSymbol, cur)) {
                    return iface;
                }
            }

            return null;
        }

    }
}
