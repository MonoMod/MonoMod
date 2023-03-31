using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;

namespace MonoMod.SourceGen.Internal.Interop
{
    [Generator]
    public class MultipurposeSlotOffsetGenerator : IIncrementalGenerator {
        private const string AttributeName = "MonoMod.Core.Interop.Attributes.MultipurposeSlotOffsetTableAttribute";

        private sealed record GenerationInfo(string MethodType, string MethodName, string Modifiers, int Depth, string HelperType);

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            var fields = context.SyntaxProvider
                .ForAttributeWithMetadataName(AttributeName, (n, ct) => true,
                (ctx, ct) => {
                    if (ctx.Attributes is [{ ConstructorArguments: [{ Value: int depth }, { Value: INamedTypeSymbol type }] }])
                    {
                        return new GenerationInfo(ctx.TargetSymbol.ContainingType.MetadataName, ctx.TargetSymbol.MetadataName,
                            ((MethodDeclarationSyntax) ctx.TargetNode).Modifiers.ToString(), depth, type.MetadataName);
                    }
                    return null;
                }).Where(i => i is not null);

            var execSrc = fields.Combine(context.CompilationProvider)!;

            context.RegisterSourceOutput(execSrc, Execute!);
        }

        private static void Execute(SourceProductionContext spc, (GenerationInfo info, Compilation compilation) tup) {
            var sb = new StringBuilder();
            var builder = new CodeBuilder(sb);
            builder.WriteHeader();

            BuildSourceFor(builder, tup, out var fieldName);
            spc.AddSource($"{fieldName}.g.cs", sb.ToString());
        }

        private static void BuildSourceFor(CodeBuilder builder, (GenerationInfo info, Compilation compilation) tup, out string fieldName) {

            var methodType = tup.compilation.GetTypeByMetadataName(tup.info.MethodType)!;

            var ctx = new TypeSourceContext(methodType);

            ctx.AppendEnterContext(builder);

            builder.Write(tup.info.Modifiers);
            builder.WriteLine($"byte[] {tup.info.MethodName}() => new byte[] {{").IncreaseIndent();

            // https://github.com/dotnet/runtime/blob/v6.0.5/src/coreclr/vm/methodtable.cpp#L318
            var maxVal = 1u << tup.info.Depth;
            for (var mask = 0u; mask < maxVal; mask++) {
                var raw = PopCount(mask);
                var index = (((mask & 3) == 2) && (raw == 1)) ? 0 : raw;

                if (index == 0) {
                    builder.WriteLine($"{tup.info.HelperType}.OffsetOfMp1(),");
                } else if (index == 1) {
                    builder.WriteLine($"{tup.info.HelperType}.OffsetOfMp2(), ");
                } else {
                    builder.WriteLine($"{tup.info.HelperType}.RegularOffset({index}), ");
                }
            }

            builder.DecreaseIndent().WriteLine("};");

            ctx.AppendExitContext(builder);

            fieldName = $"{ctx.FullContextName}.{tup.info.MethodName}";
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
