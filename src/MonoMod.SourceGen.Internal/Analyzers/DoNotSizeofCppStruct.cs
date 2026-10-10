using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using MonoMod.SourceGen.Internal.Extensions;
using System.Collections.Immutable;

namespace MonoMod.SourceGen.Internal.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DoNotSizeofCppStruct : DiagnosticAnalyzer
    {
        private const string CppStructAttribute = "MonoMod.Core.Interop.Attributes.CppStructAttribute";

        private static readonly DiagnosticDescriptor DoNotSizeofCppStructDescriptor = new(
            "MMA004",
            "Do not use the sizeof() operator on a [CppStruct]",
            "sizeof({0}) is the size of the managed mirror, not of the native type; use {0}.Layout.Size",
            "Interop",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DoNotSizeofCppStructDescriptor);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "Roslyn always passes a non-null context")]
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.ReportDiagnostics);

            context.RegisterOperationAction(ctx =>
            {
                var sizeofOp = (ISizeOfOperation)ctx.Operation;

                if (sizeofOp.TypeOperand.HasAttributeWithFullyQualifiedMetadataName(CppStructAttribute))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(DoNotSizeofCppStructDescriptor, sizeofOp.Syntax.GetLocation(),
                        sizeofOp.TypeOperand.Name));
                }
            }, OperationKind.SizeOf);
        }
    }
}
