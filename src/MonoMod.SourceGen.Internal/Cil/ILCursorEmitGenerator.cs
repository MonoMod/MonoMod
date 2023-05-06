using System.Linq;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MonoMod.SourceGen.Internal.Helpers;

namespace MonoMod.SourceGen.Internal.Cil {
    [Generator]
    public class ILCursorEmitGenerator : IIncrementalGenerator { 
        private sealed record Data(TypeContext Klass, Location? Location, string FileName, string? FileText);
        
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

            var provider = context.SyntaxProvider
                .ForAttributeWithMetadataName("MonoMod.SourceGen.Attributes.EmitParamsAttribute",
                    static (_, _) => true,
                    static (ctx, ct) => {
                        using var b = ImmutableArrayBuilder<(TypeContext Ctx, Location? Location, string Filename)>.Rent();
                        var type = GenHelpers.CreateTypeContext((INamedTypeSymbol) ctx.TargetSymbol);
                        foreach (var attr in ctx.Attributes) {
                            if (attr is { ConstructorArguments: [{ Value: string fname }] }) {
                                b.Add((type, attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation(), fname));
                            }
                        }
                        return b.ToImmutable();
                    })
                .SelectMany(static (i, _) => i)
                .Combine(context.AdditionalTextsProvider.Collect())
                .Select(static (t, ct)
                    => new Data(t.Left.Ctx, t.Left.Location, t.Left.Filename, t.Right.FirstOrDefault(text => Path.GetFileName(text.Path) == t.Left.Filename)?.GetText(ct)?.ToString()));

            context.RegisterSourceOutput(provider, Generate);
        }

        private static readonly DiagnosticDescriptor ErrNoAdditionalFile
            = new("MM.ILCursor.NoFile", "No such additional file", 
                "No additional file with the name of '{0}' was found", "", DiagnosticSeverity.Error, true);

        private static void Generate(SourceProductionContext ctx, Data data) {

            if (data.FileText is null) {
                ctx.ReportDiagnostic(Diagnostic.Create(ErrNoAdditionalFile, data.Location, data.FileName));
                return;
            }

            var stringBuilder = new StringBuilder();
            var builder = new CodeBuilder(stringBuilder);
            builder.WriteHeader();

            builder.WriteLine("using System;");
            builder.WriteLine("using System.Linq;");
            builder.WriteLine("using System.Reflection;");
            builder.WriteLine("using MonoMod.Utils;");
            builder.WriteLine("using Mono.Cecil;");
            builder.WriteLine("using Mono.Cecil.Cil;");

            data.Klass.AppendEnterContext(builder);

            Dictionary<string, List<(string type, string expr)>> typeMaps = new();
            var sections = data.FileText.Split(new string[] { "\n\n" }, StringSplitOptions.None);

            foreach (var typeMap in sections[0].Split('\n')) {
                if (typeMap.StartsWith("#", StringComparison.Ordinal))
                    continue;

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
                if (opcodeSection.StartsWith("#", StringComparison.Ordinal))
                    continue;
                var parts = opcodeSection.Split(' ');
                var opcode = parts[0];
                var opcodeFormatted = opcode.Replace("_", "");
                if (parts.Length == 1) {
                    builder.WriteLine($"/// <summary>Emit a {opcode} opcode to the current cursor position.</summary>");
                    builder.WriteLine("/// <returns>this</returns>");
                    builder.WriteLine($"public {data.Klass.InnermostType.FqName} Emit{opcodeFormatted}() => _Insert(IL.Create(OpCodes.{opcode}));");
                } else {
                    var destType = parts[1];
                    if (!typeMaps.TryGetValue(destType, out List<(string type, string expr)> types)) {
                        types = new List<(string type, string expr)>() { (destType, "operand") };
                    }
                    foreach ((string type, string expr) type in types) {
                        builder.WriteLine($"/// <summary>Emit a {opcode} opcode with a {type.type} operand to the current cursor position.</summary>");
                        builder.Write("""/// <param name="operand">The emitted instruction's operand.""");
                        if (type.type != destType) {
                            builder.Write($$""" Will be automatically converted to a <see cref="{{destType}}" />.""");
                        }
                        builder.WriteLine("</param>");
                        builder.WriteLine("/// <returns>this</returns>");
                        builder.WriteLine($"public {data.Klass.InnermostType.FqName} Emit{opcodeFormatted}({type.type} operand) => _Insert(IL.Create(OpCodes.{opcode}, {type.expr}));");
                    }
                }
                builder.WriteLine();
            }

            builder
                .CloseBlock()
                .CloseBlock();
            
            ctx.AddSource(data.Klass.FullContextName + ".g.cs", stringBuilder.ToString());
        }
    }
}
