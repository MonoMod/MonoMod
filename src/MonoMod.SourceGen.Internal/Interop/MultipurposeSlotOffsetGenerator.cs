using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using System.Threading;

namespace MonoMod.SourceGen.Internal.Interop
{
    [Generator]
    public class MultipurposeSlotOffsetGenerator : IIncrementalGenerator {
        private const string AttributeName = "MonoMod.Core.Interop.Attributes.MultipurposeSlotOffsetTableAttribute";

        private record GenerationInfo(IMethodSymbol Method, SyntaxTokenList Modifiers, int Depth, INamedTypeSymbol HelperType);

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            var fields = context.SyntaxProvider
                .CreateSyntaxProvider(IsTargetField, GetSemanticTarget)
                .Where(static m => m is not null);

            context.RegisterSourceOutput(fields, Execute!);
        }

        private static bool IsTargetField(SyntaxNode node, CancellationToken cancellationToken)
            => node is MethodDeclarationSyntax s && s.AttributeLists.Count > 0;

        private static GenerationInfo? GetSemanticTarget(GeneratorSyntaxContext ctx, CancellationToken cancellationToken) {
            var methodSyntax = (MethodDeclarationSyntax) ctx.Node;

            foreach (var attrList in methodSyntax.AttributeLists) {
                foreach (var attr in attrList.Attributes) {
                    if (ctx.SemanticModel.GetSymbolInfo(attr, cancellationToken).Symbol is not IMethodSymbol attrSym) {
                        // we couldn't get the model for the attribute, for some reason
                        continue;
                    }

                    var type = attrSym.ContainingType;
                    if (type.ToDisplayString() == AttributeName) {
                        if (attr.ArgumentList is not { } attrArgs) {
                            // what?
                            //Console.WriteLine("[SRC] Arg list doesn't exist");
                            continue;
                        }

                        if (attrArgs.Arguments.Count < 2) {
                            // what?
                            //Console.WriteLine("[SRC] Arg list too short");
                            continue;
                        }

                        var firstArg = attrArgs.Arguments[0];
                        var argValue = ctx.SemanticModel.GetConstantValue(firstArg.Expression, cancellationToken);
                        if (!argValue.HasValue) {
                            continue;
                        }
                        if (argValue.Value is not int num) {
                            // what?
                            continue;
                        }

                        var secondArg = attrArgs.Arguments[1];
                        if (secondArg.Expression is not TypeOfExpressionSyntax typeOf) {
                            // what?
                            continue;
                        }

                        if (ctx.SemanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol methSym) {
                            //Console.WriteLine($"[SRC] No method ({methodSyntax} ({methodSyntax.GetType()}))");
                            // we couldn't get the model for the field, for some reason
                            continue;
                        }

                        if (ctx.SemanticModel.GetSymbolInfo(typeOf.Type, cancellationToken).Symbol is not INamedTypeSymbol helperType) {
                            //Console.WriteLine($"[SRC] No helper type");
                            continue;
                        }

                        return new(methSym, methodSyntax.Modifiers, num, helperType);
                    }
                }
            }

            return null;
        }

        private static void Execute(SourceProductionContext spc, GenerationInfo field) {
            var sb = new StringBuilder();
            var builder = new CodeBuilder(sb);
            builder.WriteHeader();

            BuildSourceFor(builder, field, out var fieldName);
            spc.AddSource($"{fieldName}.g.cs", sb.ToString());
        }

        private static void BuildSourceFor(CodeBuilder builder, GenerationInfo info, out string fieldName) {

            var methodSymbol = info.Method;
            var helperSymbol = info.HelperType;

            var helperName = helperSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var ctx = new TypeSourceContext(methodSymbol);

            ctx.AppendEnterContext(builder);

            foreach (var mod in info.Modifiers) {
                builder.Write(mod.Text).Write(' ');
            }
            builder.WriteLine($"byte[] {methodSymbol.Name}() => new byte[] {{").IncreaseIndent();

            // https://github.com/dotnet/runtime/blob/v6.0.5/src/coreclr/vm/methodtable.cpp#L318
            var maxVal = 1u << info.Depth;
            for (var mask = 0u; mask < maxVal; mask++) {
                var raw = PopCount(mask);
                var index = (((mask & 3) == 2) && (raw == 1)) ? 0 : raw;

                if (index == 0) {
                    builder.WriteLine($"{helperName}.OffsetOfMp1(),");
                } else if (index == 1) {
                    builder.WriteLine($"{helperName}.OffsetOfMp2(), ");
                } else {
                    builder.WriteLine($"{helperName}.RegularOffset({index}), ");
                }
            }

            builder.DecreaseIndent().WriteLine("};");

            ctx.AppendExitContext(builder);

            fieldName = $"{ctx.FullContextName}.{methodSymbol.Name}";
        }

        private static int PopCount(uint value) {
            const uint c1 = 0x_55555555u;
            const uint c2 = 0x_33333333u;
            const uint c3 = 0x_0F0F0F0Fu;
            const uint c4 = 0x_01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;

            return (int) value;
        }
    }
}
