using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace MonoMod.SourceGen.Internal.Utils
{
    [Generator]
    public class FastDelegateInvokersGenerator : IIncrementalGenerator
    {
        private const string AttributeName = "MonoMod.Cil.GetFastDelegateInvokersArrayAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var methods = context.SyntaxProvider
                .ForAttributeWithMetadataName(AttributeName,
                    (n, ct) => true,
                    (ctx, ct)
                        => ctx.Attributes
                            .Select(a
                                => a.ConstructorArguments is [{ Value: int maxArgs }]
                                    ? new GeneratorMethod(GenHelpers.CreateTypeContext(ctx.TargetSymbol.ContainingType), ctx.TargetSymbol.Name,
                                        ((MethodDeclarationSyntax)ctx.TargetNode).Modifiers.ToString(), maxArgs)
                                    : null).Where(d => d is not null))
                .SelectMany((e, _) => e);

            context.RegisterSourceOutput(methods, Execute!);
        }

        private sealed record GeneratorMethod(TypeContext Type, string MethodName, string Modifiers, int MaxArgs);
        private void Execute(SourceProductionContext ctx, GeneratorMethod method)
        {
            var sb = new StringBuilder();
            var builder = new CodeBuilder(sb);
            _ = builder.WriteHeader();

            BuildSourceFor(builder, method, out var methodName);
            ctx.AddSource($"{methodName}.g.cs", sb.ToString());
        }

        [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider",
            Justification = "SourceGen is a Roslyn extension, not using specific localization settings for integers isn't that important.")]
        private static void BuildSourceFor(CodeBuilder builder, GeneratorMethod info, out string methodName)
        {
            _ = builder.WriteLine("using BindingFlags = global::System.Reflection.BindingFlags;");
            _ = builder.WriteLine("using MethodInfo = global::System.Reflection.MethodInfo;");
            _ = builder.WriteLine("using Type = global::System.Type;");
            _ = builder.WriteLine("using Helpers = global::MonoMod.Utils.Helpers;");

            info.Type.AppendEnterContext(builder);

            methodName = $"{info.Type.FullContextName}.{info.MethodName}";

            const string ReturnType = "(MethodInfo, Type)";
            var selfTypeof = $"typeof({info.Type.InnermostType.FqName})";

            // first, we want to generate the getter method
            _ = builder.Write(info.Modifiers).Write(' ');
            _ = builder.WriteLine($"{ReturnType}[] {info.MethodName}() {{").IncreaseIndent();

            _ = builder.WriteLine($"var array = new {ReturnType}[{info.MaxArgs << 2}];");
            for (var i = 0; i < (info.MaxArgs << 2); i++)
            {
                var name = ComputeNameForIdx(i);
                _ = builder.Write($"array[{i}] = (").Write(selfTypeof).Write(".GetMethod(\"Invoke").Write(name)
                    .Write("\", BindingFlags.NonPublic | BindingFlags.Static)!, ")
                    .Write("typeof(").Write(name).Write('<');
                var numArgs = (i & 1) + (i >> 2);
                _ = builder.Write(new string(',', numArgs));
                _ = builder.WriteLine(">));");
            }
            _ = builder.WriteLine("return array;");

            _ = builder.DecreaseIndent().WriteLine("}");

            _ = builder.WriteLine();

            var genericArgsBuilder = new StringBuilder();

            // fun fact: with nullable annotations, the compiler adds an attribute to EVERY SINGLE PARAMETER
            // we'll just disable nullable annocations here to avoid this
            _ = builder.WriteLine("#nullable disable").WriteLine();

            // now we generate the types and methods themselves
            for (var i = 0; i < (info.MaxArgs << 2); i++)
            {
                var name = ComputeNameForIdx(i);
                // generic parameters are TResult, T0, ...
                var hasResult = (i & 1) != 0;
                var firstIsByRef = (i & 2) != 0;
                var numRemaining = i >> 2;

                genericArgsBuilder.Clear();

                // write the delegate definition
                _ = builder.Write("private delegate ").Write(hasResult ? "TResult" : "void").Write(' ').Write(name);
                genericArgsBuilder.Append('<');
                if (hasResult)
                    genericArgsBuilder.Append("TResult, ");
                genericArgsBuilder.Append("T0");
                if (numRemaining > 0)
                    genericArgsBuilder.Append(", ");
                for (var j = 0; j < numRemaining; j++)
                {
                    genericArgsBuilder.Append($"T{j + 1}");
                    if (j + 1 < numRemaining)
                        genericArgsBuilder.Append(", ");
                }
                genericArgsBuilder.Append('>');
                var genericArgs = genericArgsBuilder.ToString();

                _ = builder.Write(genericArgs).Write('(');
                if (firstIsByRef)
                    _ = builder.Write("ref ");

                for (var j = 0; j < numRemaining + 1; j++)
                {
                    _ = builder.Write($"T{j} _{j}");
                    if (j < numRemaining)
                        _ = builder.Write(", ");
                }
                _ = builder.WriteLine(");");

                // write the invoker method
                _ = builder
                    .Write("private static ").Write(hasResult ? "TResult" : "void").Write(" Invoke").Write(name)
                    .Write(genericArgs).Write('(');
                if (firstIsByRef)
                    _ = builder.Write("ref ");
                for (var j = 0; j < numRemaining + 1; j++)
                {
                    _ = builder.Write($"T{j} _{j}, ");
                }
                // now the last arg, which is the delegate arg
                _ = builder
                    .Write(name).Write(genericArgs).WriteLine(" del)")
                    .IncreaseIndent().Write("=> Helpers.ThrowIfNull(del)(");
                if (firstIsByRef)
                    builder.Write("ref ");
                for (var j = 0; j < numRemaining + 1; j++)
                {
                    _ = builder.Write($"_{j}");
                    if (j < numRemaining)
                        _ = builder.Write(", ");
                }
                _ = builder.WriteLine(");").DecreaseIndent();
                _ = builder.WriteLine();
            }

            _ = builder.WriteLine("#nullable enable");

            info.Type.AppendExitContext(builder);
        }

        private static string ComputeNameForIdx(int idx)
        {
            // this index is structured as follows (low to high bits)
            //     xyzzzzz...
            // x: has non-void return
            // y: first param is byref
            // z: number of parameters AFTER the first

            return ((idx & 1) == 0 ? "Void" : "Type") + ((idx & 2) == 0 ? "Val" : "Ref") + ((idx >> 2) + 1);
        }
    }
}
