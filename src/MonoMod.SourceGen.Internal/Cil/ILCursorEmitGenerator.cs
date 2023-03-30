using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MonoMod.SourceGen.Internal.Cil {
    [Generator]
    public class ILCursorEmitGenerator : IIncrementalGenerator { 
        private record Data(INamedTypeSymbol Klass, string FileName, string FileText);
        
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            context.RegisterPostInitializationOutput(static ctx => ctx.AddSource("EmitParamsAttribute.g.cs", 
                """
                namespace MonoMod.SourceGen.Attributes {
                    internal class EmitParamsAttribute : global::System.Attribute {
                        public EmitParamsAttribute(string fileName) { }
                    }
                }
                """
                )
            );

            IncrementalValuesProvider<Data> provider = context.SyntaxProvider.CreateSyntaxProvider(IsTargetClass, GetSemanticData)
                .Where(x => x is not null)
                .Combine(context.AdditionalTextsProvider.Collect())
                .Select((tup, _) => (tup.Item1, Right: tup.Item2.First(t => Path.GetFileName(t.Path) == tup.Item1!.FileName)))
                .Where(tup => tup.Item2 is not null)
                .Select((tup, token) => tup.Item1! with { FileText = tup.Item2!.GetText(token)!.ToString() });
            
            context.RegisterSourceOutput(provider, Generate);
        }

        private static bool IsTargetClass(SyntaxNode node, CancellationToken token)
            => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

        private static Data? GetSemanticData(GeneratorSyntaxContext ctx, CancellationToken token) {
            var classSyntax = (ClassDeclarationSyntax) ctx.Node;

            foreach (AttributeSyntax? attr in classSyntax.AttributeLists.SelectMany(l => l.Attributes)) {
                if (ctx.SemanticModel.GetSymbolInfo(attr, token).Symbol is not IMethodSymbol attrSym) {
                    continue;
                }

                INamedTypeSymbol? attrType = attrSym.ContainingType;

                if (attrType.ToDisplayString() != "MonoMod.SourceGen.Attributes.EmitParamsAttribute") {
                    continue;
                }

                if (attr is not { ArgumentList.Arguments.Count: 1 }) {
                    continue;
                }

                var fileName = (string)ctx.SemanticModel.GetConstantValue(attr.ArgumentList.Arguments[0].Expression, token).Value!;

                return new Data((INamedTypeSymbol) ctx.SemanticModel.GetDeclaredSymbol(classSyntax, token)!, fileName, null!);
            }
            return null;
        }

        private static void Generate(SourceProductionContext ctx, Data data) {
            var klassName = data.Klass.Name;
            
            var stringBuilder = new StringBuilder();
            var builder = new CodeBuilder(stringBuilder);
            builder.WriteHeader();

            builder.WriteLine("using System;");
            builder.WriteLine("using System.Linq;");
            builder.WriteLine("using System.Reflection;");
            builder.WriteLine("using MonoMod.Utils;");
            builder.WriteLine("using Mono.Cecil;");
            builder.WriteLine("using Mono.Cecil.Cil;");

            builder.WriteLine($$"""namespace {{data.Klass.ContainingNamespace}} {""");
            builder.IncreaseIndent();
            builder.WriteLine($$"""partial class {{klassName}} {""");
            builder.IncreaseIndent();

            Dictionary<string, List<(string type, string expr)>> typeMaps = new();
            var sections = data.FileText.Split(new string[] { "\n\n" }, StringSplitOptions.None);

            foreach (var typeMap in sections[0].Split('\n')) {
                var firstSpace = typeMap.IndexOf(' ');
                var mapping = typeMap.Substring(0, firstSpace);
                var expr = typeMap.Substring(firstSpace + 1);
                var mappingSections = mapping.Split(new[] { "->" }, StringSplitOptions.None);
                var source = mappingSections[0];
                var dest = mappingSections[1];
                if (!typeMaps.TryGetValue(dest, out List<(string type, string expr)> list)) {
                    typeMaps[dest] = list = new List<(string type, string expr)>();
                    list.Add((dest, "operand"));
                }
                list.Add((source, expr));
            }

            foreach (var opcodeSection in sections[1].Split('\n')) {
                var parts = opcodeSection.Split(' ');
                var opcode = parts[0];
                var opcodeFormatted = opcode.Replace("_", "");
                if (parts.Length == 1) {
                    builder.WriteLine($"public {klassName} Emit{opcodeFormatted}() => _Insert(IL.Create(OpCodes.{opcode}));");
                } else {
                    var destType = parts[1];
                    if (!typeMaps.TryGetValue(destType, out List<(string type, string expr)> types)) {
                        types = new List<(string type, string expr)>() { (destType, "operand") };
                    }
                    foreach ((string type, string expr) type in types) {
                        builder.WriteLine($"public {klassName} Emit{opcodeFormatted}({type.type} operand) => _Insert(IL.Create(OpCodes.{opcode}, {type.expr}));");
                    }
                }
                builder.WriteLine();
            }

            builder.DecreaseIndent();
            builder.WriteLine("}");
            builder.DecreaseIndent();
            builder.WriteLine("}");
            
            ctx.AddSource(data.Klass.ToDisplayString() + ".g.cs", stringBuilder.ToString());
        }
    }
}
