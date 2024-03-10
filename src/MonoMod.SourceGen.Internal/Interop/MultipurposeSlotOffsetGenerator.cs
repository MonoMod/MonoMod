using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;

namespace MonoMod.SourceGen.Internal.Interop
{
    [Generator]
    public class MultipurposeSlotOffsetGenerator : IIncrementalGenerator
    {
        private const string AttributeName = "MonoMod.Core.Interop.Attributes.MultipurposeSlotOffsetTableAttribute";

        private sealed record GenerationInfo(TypeContext Type, string MethodName, string Modifiers, int Depth, string HelperType);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var fields = context.SyntaxProvider
                .ForAttributeWithMetadataName(AttributeName, (n, ct) => true,
                (ctx, ct) =>
                {
                    if (ctx.Attributes is [{ ConstructorArguments: [{ Value: int depth }, { Value: INamedTypeSymbol type }] }])
                    {
                        return new GenerationInfo(GenHelpers.CreateTypeContext(ctx.TargetSymbol.ContainingType), ctx.TargetSymbol.MetadataName,
                            ((MethodDeclarationSyntax)ctx.TargetNode).Modifiers.ToString(), depth, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                    return null;
                }).Where(i => i is not null);

            context.RegisterSourceOutput(fields, Execute!);
        }

        private static void Execute(SourceProductionContext spc, GenerationInfo info)
        {
            var sb = new StringBuilder();
            var builder = new CodeBuilder(sb);
            builder.WriteHeader();

            BuildSourceFor(builder, info, out var fieldName);
            spc.AddSource($"{fieldName}.g.cs", sb.ToString());
        }

        private static void BuildSourceFor(CodeBuilder builder, GenerationInfo info, out string fieldName)
        {
            info.Type.AppendEnterContext(builder);

            builder.Write(info.Modifiers).Write(' ');
            builder.WriteLine($"byte[] {info.MethodName}() => new byte[] {{").IncreaseIndent();

            // https://github.com/dotnet/runtime/blob/v6.0.5/src/coreclr/vm/methodtable.cpp#L318
            var maxVal = 1u << info.Depth;
            for (var mask = 0u; mask < maxVal; mask++)
            {
                var raw = PopCount(mask);
                var index = (((mask & 3) == 2) && (raw == 1)) ? 0 : raw;

                if (index == 0)
                {
                    builder.WriteLine($"{info.HelperType}.OffsetOfMp1(),");
                }
                else if (index == 1)
                {
                    builder.WriteLine($"{info.HelperType}.OffsetOfMp2(), ");
                }
                else
                {
                    builder.WriteLine($"{info.HelperType}.RegularOffset({index}), ");
                }
            }

            builder.DecreaseIndent().WriteLine("};");

            info.Type.AppendExitContext(builder);

            fieldName = $"{info.Type.FullContextName}.{info.MethodName}";
        }

        private static int PopCount(uint value)
        {
            const uint c1 = 0x_55555555u;
            const uint c2 = 0x_33333333u;
            const uint c3 = 0x_0F0F0F0Fu;
            const uint c4 = 0x_01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;

            return (int)value;
        }
    }
}
