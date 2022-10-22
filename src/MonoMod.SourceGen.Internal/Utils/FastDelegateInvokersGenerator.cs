using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Threading;
using System.Linq;
using System.Text;

namespace MonoMod.SourceGen.Internal.Utils {
    [Generator]
    public class FastDelegateInvokersGenerator : IIncrementalGenerator {
        private const string AttributeName = "MonoMod.Cil.GetFastDelegateInvokersArrayAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            var methods = context.SyntaxProvider
                .CreateSyntaxProvider(IsPotentialTarget, GetSemanticTarget)
                .Where(static m => m is not null);

            context.RegisterSourceOutput(methods, Execute!);
        }

        private bool IsPotentialTarget(SyntaxNode node, CancellationToken cancellation)
            => node is MethodDeclarationSyntax s && s.AttributeLists.Count > 0;

        private sealed record GeneratorMethod(IMethodSymbol Method, SyntaxTokenList Modifiers, int MaxArgs);

        private GeneratorMethod? GetSemanticTarget(GeneratorSyntaxContext ctx, CancellationToken cancellationToken) {
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
                            Console.WriteLine("[SRC] Arg list doesn't exist");
                            continue;
                        }

                        if (attrArgs.Arguments.Count < 1) {
                            // what?
                            Console.WriteLine("[SRC] Arg list too short");
                            continue;
                        }

                        var firstArg = attrArgs.Arguments[0];
                        var argValue = ctx.SemanticModel.GetConstantValue(firstArg.Expression, cancellationToken);
                        if (!argValue.HasValue) {
                            continue;
                        }
                        if (argValue.Value is not int numArgs) {
                            // what?
                            continue;
                        }

                        if (ctx.SemanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol methSym) {
                            Console.WriteLine($"[SRC] No method ({methodSyntax} ({methodSyntax.GetType()}))");
                            // we couldn't get the model for the field, for some reason
                            continue;
                        }

                        return new(methSym, methodSyntax.Modifiers, numArgs);
                    }
                }
            }

            return null;
        }

        private void Execute(SourceProductionContext ctx, GeneratorMethod method) {
            var sb = new StringBuilder();
            var builder = new CodeBuilder(sb);
            builder.WriteHeader();

            BuildSourceFor(builder, method, out var methodName);
            ctx.AddSource($"{methodName}.g.cs", sb.ToString());
        }

        private void BuildSourceFor(CodeBuilder builder, GeneratorMethod method, out string methodName) {
            var methodSymbol = method.Method;
            var maxArgs = method.MaxArgs;

            builder.WriteLine("using BindingFlags = global::System.Reflection.BindingFlags;");
            builder.WriteLine("using MethodInfo = global::System.Reflection.MethodInfo;");
            builder.WriteLine("using Type = global::System.Type;");
            builder.WriteLine("using Helpers = global::MonoMod.Utils.Helpers;");

            var ctx = new TypeSourceContext(methodSymbol);
            ctx.AppendEnterContext(builder);

            methodName = $"{ctx.FullContextName}.{methodSymbol.Name}";

            const string ReturnType = "(MethodInfo, Type)";
            var selfTypeof = $"typeof({ctx.InnermostType.Name})";

            // first, we want to generate the getter method
            foreach (var mod in method.Modifiers) {
                builder.Write(mod.Text).Write(' ');
            }
            builder.WriteLine($"{ReturnType}[] {methodSymbol.Name}() {{").IncreaseIndent();

            builder.WriteLine($"var array = new {ReturnType}[{maxArgs << 2}];");
            for (var i = 0; i < (maxArgs << 2); i++) {
                var name = ComputeNameForIdx(i);
                builder.Write($"array[{i}] = (").Write(selfTypeof).Write(".GetMethod(\"Invoke").Write(name)
                    .Write("\", BindingFlags.NonPublic | BindingFlags.Static)!, ")
                    .Write("typeof(").Write(name).Write('<');
                var numArgs = (i & 1) + (i >> 2);
                builder.Write(new string(',', numArgs));
                builder.WriteLine(">));");
            }
            builder.WriteLine("return array;");

            builder.DecreaseIndent().WriteLine("}");

            builder.WriteLine();

            var genericArgsBuilder = new StringBuilder();

            // fun fact: with nullable annotations, the compiler adds an attribute to EVERY SINGLE PARAMETER
            // we'll just disable nullable annocations here to avoid this
            builder.WriteLine("#nullable disable").WriteLine();

            // now we generate the types and methods themselves
            for (var i = 0; i < (maxArgs << 2); i++) {
                var name = ComputeNameForIdx(i);
                // generic parameters are TResult, T0, ...
                var hasResult = (i & 1) != 0;
                var firstIsByRef = (i & 2) != 0;
                var numRemaining = i >> 2;

                genericArgsBuilder.Clear();

                // write the delegate definition
                builder.Write("private delegate ").Write(hasResult ? "TResult" : "void").Write(' ').Write(name);
                genericArgsBuilder.Append('<');
                if (hasResult)
                    genericArgsBuilder.Append("TResult, ");
                genericArgsBuilder.Append("T0");
                if (numRemaining > 0)
                    genericArgsBuilder.Append(", ");
                for (var j = 0; j < numRemaining; j++) {
                    genericArgsBuilder.Append($"T{j + 1}");
                    if (j + 1 < numRemaining)
                        genericArgsBuilder.Append(", ");
                }
                genericArgsBuilder.Append('>');
                var genericArgs = genericArgsBuilder.ToString();

                builder.Write(genericArgs).Write('(');
                if (firstIsByRef)
                    builder.Write("ref ");

                for (var j = 0; j < numRemaining + 1; j++) {
                    builder.Write($"T{j} _{j}");
                    if (j < numRemaining)
                        builder.Write(", ");
                }
                builder.WriteLine(");");

                // write the invoker method
                builder.Write("private static ").Write(hasResult ? "TResult" : "void").Write(" Invoke").Write(name)
                    .Write(genericArgs).Write('(');
                if (firstIsByRef)
                    builder.Write("ref ");
                for (var j = 0; j < numRemaining + 1; j++) {
                    builder.Write($"T{j} _{j}, ");
                }
                // now the last arg, which is the delegate arg
                builder.Write(name).Write(genericArgs).WriteLine(" del)")
                    .IncreaseIndent().Write("=> Helpers.ThrowIfNull(del)(");
                if (firstIsByRef)
                    builder.Write("ref ");
                for (var j = 0; j < numRemaining + 1; j++) {
                    builder.Write($"_{j}");
                    if (j < numRemaining)
                        builder.Write(", ");
                }
                builder.WriteLine(");").DecreaseIndent();
                builder.WriteLine();
            }

            builder.WriteLine("#nullable enable");

            ctx.AppendExitContext(builder);
        }

        private static string ComputeNameForIdx(int idx) {
            // this index is structured as follows (low to high bits)
            //     xyzzzzz...
            // x: has non-void return
            // y: first param is byref
            // z: number of parameters AFTER the first

            return ((idx & 1) == 0 ? "Void" : "Type") + ((idx & 2) == 0 ? "Val" : "Ref") + ((idx >> 2) + 1);
        }
    }
}
