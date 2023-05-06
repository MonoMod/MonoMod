using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using MonoMod.SourceGen.Internal.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace MonoMod.SourceGen.Internal.Cil {
    [Generator]
    public sealed class ILOverloadGenerator : IIncrementalGenerator {

        private const string KindCursor = "ILCursor";
        private const string KindMatcher = "ILMatcher";

        private sealed record TypeWithEmitOverloads(TypeContext Type, Location? Location, string SourceFile, string ReadFromFile, string Kind);

        private record struct ParsedConversionDef(string FromType, string ToType, string ConvertExpr);
        private record struct ConversionDefSet(string Name, EquatableArray<ParsedConversionDef> Defs);
        private record struct OpcodeDef(string Opcode, string Formatted, string? ArgumentType);
        private sealed record ParsedDefFile(
            EquatableArray<string> Usings,
            EquatableArray<ConversionDefSet> ConversionDefs,
            EquatableArray<OpcodeDef> Opcodes);

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            context.RegisterPostInitializationOutput(static ctx => ctx.AddSource("EmitILOverloadsAttribute.g.cs",
                $$"""
                namespace MonoMod.SourceGen.Attributes {
                    [global::System.AttributeUsageAttribute(global::System.AttributeTargets.Class)]
                    internal sealed class EmitILOverloadsAttribute : global::System.Attribute {
                        public EmitILOverloadsAttribute(string filename, string kind) { }
                    }

                    internal static class ILOverloadKind {
                        public const string Cursor = "{{KindCursor}}";
                        public const string Matcher = "{{KindMatcher}}";
                    }
                }
                """
                )
            );

            var emitIlOverloadsProvider = context.SyntaxProvider
                .ForAttributeWithMetadataName("MonoMod.SourceGen.Attributes.EmitILOverloadsAttribute",
                    static (n, _) => n.IsKind(SyntaxKind.ClassDeclaration),
                    static (ctx, ct) => {
                        using var b = ImmutableArrayBuilder<TypeWithEmitOverloads>.Rent();
                        var type = GenHelpers.CreateTypeContext((INamedTypeSymbol) ctx.TargetSymbol);
                        var sourceFile = ctx.TargetNode.SyntaxTree.FilePath;
                        foreach (var attr in ctx.Attributes) {
                            if (attr is { ConstructorArguments: [{ Value: string fname }, { Value: string kind }] }) {
                                b.Add(new TypeWithEmitOverloads(type, attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation(), sourceFile, fname, kind));
                            }
                        }
                        return b.ToImmutable();
                    })
                .SelectMany(static (i, _) => i);

            var overloadsWithBadKind = emitIlOverloadsProvider
                .Where(static t => t.Kind is not KindCursor and not KindMatcher);

            var overloadsWithValidKind = emitIlOverloadsProvider
                .Where(static t => t.Kind is KindCursor or KindMatcher);

            context.RegisterSourceOutput(overloadsWithBadKind, static (spc, info) => {
                spc.ReportDiagnostic(Diagnostic.Create(ErrInvalidKind, info.Location, info.Kind));
            });

            var neededDefinitionPathsProvider = overloadsWithValidKind
                .SelectMany(static (t, ct) => ImmutableArray.Create(
                    Path.Combine(Path.GetDirectoryName(t.SourceFile), t.ReadFromFile),
                    t.ReadFromFile
                ));

            var neededSourceTextsProvider = context.AdditionalTextsProvider
                .Combine(neededDefinitionPathsProvider.Collect())
                .Where(static t => t.Right.Contains(t.Left.Path) || t.Right.Contains(Path.GetFileName(t.Left.Path)))
                .Select(static (t, ct) => (t.Left.Path, Text: t.Left.GetText(ct)))
                .Where(static t => t.Text is not null)!
                .WithComparer(SourceTextTupleComparer.Instance);

            var parsedSourceTextsProvider = neededSourceTextsProvider.Select(ParseDefsFile);

            var emitInfoWithMatchedSourceProvider = overloadsWithValidKind
                .Combine(parsedSourceTextsProvider.Collect())
                .Select(static (t, ct)
                    => (Info: t.Left, Defs: t.Right.FirstOrDefault(x => x.Path == t.Left.ReadFromFile).Defs 
                            ?? t.Right.FirstOrDefault(x => Path.GetFileName(x.Path) == t.Left.ReadFromFile).Defs));

            var overloadsWithNoFile = emitInfoWithMatchedSourceProvider
                .Where(static t => t.Defs is null)
                .Select(static (t, ct) => t.Info);

            var overloadsWithFile = emitInfoWithMatchedSourceProvider
                .Where(static t => t.Defs is not null);

            context.RegisterSourceOutput(overloadsWithNoFile, static (spc, info) => {
                spc.ReportDiagnostic(Diagnostic.Create(ErrNoAdditionalFile, info.Location, info.ReadFromFile));
            });

            var overloadsWithCursorKind = overloadsWithFile.Where(static t => t.Info.Kind is KindCursor);
            var overloadsWithMatcherKind = overloadsWithFile.Where(static t => t.Info.Kind is KindMatcher);

            context.RegisterSourceOutput(overloadsWithCursorKind, GenerateCursorKind);
            context.RegisterSourceOutput(overloadsWithMatcherKind, GenerateMatcherKind);
        }

        private static readonly DiagnosticDescriptor ErrNoAdditionalFile
            = new("MM.ILOverload.NoFile", "No such additional file",
                "No additional file with the name of '{0}' was found", "", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor ErrInvalidKind
            = new("MM.ILOverload.BadKind", "Invalid emit kind",
                "Invalid emit kind '{0}'", "", DiagnosticSeverity.Error, true);

        private enum ParseState {
            None,
            Using,
            Conversions,
            Opcodes,
        }

        private static (string Path, ParsedDefFile Defs) ParseDefsFile((string Path, SourceText Text) text, System.Threading.CancellationToken ct) {
            using var reader = new SourceTextReader(text.Text);

            using var usingsBuilder = ImmutableArrayBuilder<string>.Rent();
            using var conversionDefSetBuilder = ImmutableArrayBuilder<ConversionDefSet>.Rent();
            using var conversionDefsBuilder = ImmutableArrayBuilder<ParsedConversionDef>.Rent();
            using var opcodeDefsBuilder = ImmutableArrayBuilder<OpcodeDef>.Rent();

            string? line;
            var state = ParseState.None;
            var sectArg = "";

            while ((line = reader.ReadLine()) is not null) {
                ct.ThrowIfCancellationRequested();
                line = line.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("[", StringComparison.Ordinal)) {

                    // terminate the last section, if there is anything to do for that
                    switch (state) {
                        case ParseState.Conversions:
                            conversionDefSetBuilder.Add(new(sectArg, conversionDefsBuilder.ToImmutable()));
                            conversionDefsBuilder.Clear();
                            break;
                    }

                    // section header
                    var endIdx = line.IndexOf(']');
                    if (endIdx < 0)
                        endIdx = line.Length;

                    var sectName = line.Substring(1, endIdx - 1);
                    var sectNameParts = sectName.Split(' ');
                    if (sectNameParts.Length <= 0)
                        continue; // continue on error

                    state = sectNameParts[0] switch {
                        "Using" => ParseState.Using,
                        "Conversions" => ParseState.Conversions,
                        "Opcodes" => ParseState.Opcodes,
                        _ => ParseState.None,
                    };

                    sectArg = sectNameParts.Length > 1 ? sectNameParts[1] : "";
                } else {
                    // section body
                    switch (state) {
                        case ParseState.Using:
                            usingsBuilder.Add(line);
                            break;
                        case ParseState.Conversions: {
                                var typeSepIdx = line.IndexOf("->", StringComparison.Ordinal);
                                if (typeSepIdx < 0)
                                    continue;
                                var fromType = line.Substring(0, typeSepIdx).Trim();

                                var opSepIdx = line.IndexOf(':', typeSepIdx + 2);
                                if (typeSepIdx < 0)
                                    continue;
                                var toType = line.Substring(typeSepIdx + 2, opSepIdx - typeSepIdx - 2).Trim();

                                var convertExpr = line.Substring(opSepIdx + 1).Trim();
                                conversionDefsBuilder.Add(new(fromType, toType, convertExpr));
                                break;
                            }
                        case ParseState.Opcodes: {
                                var split = line.Split(' ');
                                if (split.Length < 1)
                                    continue;
                                opcodeDefsBuilder.Add(new(split[0], split[0].Replace("_", ""), split.Length > 1 ? split[1] : null));
                                break;
                            }
                    }
                }
            }

            // terminate the last section, if there is anything to do for that
            switch (state) {
                case ParseState.Conversions:
                    conversionDefSetBuilder.Add(new(sectArg, conversionDefsBuilder.ToImmutable()));
                    conversionDefsBuilder.Clear();
                    break;
            }

            return (text.Path, Defs: new ParsedDefFile(usingsBuilder.ToImmutable(), conversionDefSetBuilder.ToImmutable(), opcodeDefsBuilder.ToImmutable()));
        }

        private sealed class SourceTextTupleComparer : IEqualityComparer<(string Path, SourceText Text)> {
            public static readonly SourceTextTupleComparer Instance = new();

            public bool Equals((string, SourceText) x, (string, SourceText) y)
                => x.Item1 == y.Item1 && x.Item2.ContentEquals(y.Item2);

            public int GetHashCode((string, SourceText) obj) {
                var hc = new HashCode();
                hc.Add(obj.Item1);
                hc.Add(obj.Item2.ChecksumAlgorithm);
                foreach (var checksum in obj.Item2.GetChecksum()) {
                    hc.Add(checksum);
                }
                return hc.ToHashCode();
            }
        }

        private static void EmitUsings(CodeBuilder builder, ParsedDefFile defs) {
            foreach (var use in defs.Usings) {
                _ = builder.WriteLine($"using {use};");
            }
            _ = builder.WriteLine();
        }

        private static void GenerateCursorKind(SourceProductionContext spc, (TypeWithEmitOverloads type, ParsedDefFile defs) t) {
            var (type, defs) = t;

            var sb = new StringBuilder();
            var builder = new CodeBuilder(sb);

            builder.WriteHeader();
            EmitUsings(builder, defs);

            type.Type.AppendEnterContext(builder);

            var conversions = defs.ConversionDefs.FirstOrDefault(c => c.Name == KindCursor).Defs.AsImmutableArray();
            if (conversions.IsDefault)
                conversions = ImmutableArray.Create<ParsedConversionDef>();

            foreach (var op in defs.Opcodes) {
                if (op.ArgumentType is null) {
                    builder
                        .WriteLine($"""/// <summary>Emit a <see cref="OpCodes.{op.Opcode}"/> opcode to the current cursor position.</summary>""")
                        .WriteLine("/// <returns>this</returns>")
                        .WriteLine($"public {type.Type.InnermostType.FqName} Emit{op.Formatted}() => _Insert(IL.Create(OpCodes.{op.Opcode}));")
                        .WriteLine();
                } else {
                    _ = builder.WriteLine($"#region {op.Opcode}");

                    EmitMethodWithArg(builder, type.Type.InnermostType.FqName, op, op.ArgumentType, op.ArgumentType, "operand");

                    foreach (var conv in conversions) {
                        if (conv.ToType != op.ArgumentType)
                            continue;

                        EmitMethodWithArg(builder, type.Type.InnermostType.FqName, op, conv.FromType, conv.ToType, conv.ConvertExpr);
                    }

                    _ = builder
                        .WriteLine($"#endregion")
                        .WriteLine();

                    static void EmitMethodWithArg(CodeBuilder builder, string selfFqName, OpcodeDef op, string argType, string targetType, string argExpr) {
                        builder
                            .WriteLine($"/// <summary>Emit a <see cref=\"OpCodes.{op.Opcode}\"/> opcode with a <see cref=\"{argType}\"/> operand to the current cursor position.</summary>")
                            .Write("""/// <param name="operand">The emitted instruction's operand.""");
                        if (argType != targetType) {
                            builder.Write($$""" Will be automatically converted to a <see cref="{{targetType}}" />.""");
                        }
                        builder.WriteLine("</param>")
                            .WriteLine("/// <returns>this</returns>")
                            .WriteLine($"public {selfFqName} Emit{op.Formatted}({argType} operand) => _Insert(IL.Create(OpCodes.{op.Opcode}, {argExpr}));")
                            .WriteLine();
                    }
                }
            }

            type.Type.AppendExitContext(builder);

            spc.AddSource("Cursor." + type.Type.FullContextName + ".g.cs", sb.ToString());
        }

        private static void GenerateMatcherKind(SourceProductionContext spc, (TypeWithEmitOverloads type, ParsedDefFile defs) t) {
            var (type, defs) = t;


            spc.AddSource("Matcher." + type.Type.FullContextName + ".g.cs", "#error Matcher kind is NYI.");
        }

    }
}
