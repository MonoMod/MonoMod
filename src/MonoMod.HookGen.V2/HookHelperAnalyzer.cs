using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;

namespace MonoMod.HookGen.V2 {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HookHelperAnalyzer : DiagnosticAnalyzer {

        private const string Category = "MonoMod.HookGen";

        private static readonly DiagnosticDescriptor ProjectDoesNotReferenceRuntimeDetour = new(
            "HookGen0001",
            "Assembly does not reference MonoMod.RuntimeDetour",
            "Assembly '{0}' does not reference MonoMod.RuntimeDetour, generated helpers will not compile",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: ["CompilationEnd"]);

        private static readonly DiagnosticDescriptor ReferencedTypeIsInThisAssembly = new(
            "HookGen0002",
            "Referenced type is defined in this assembly",
            "Type '{0}' is declared in this assembly. Helpers will not be generated for it.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor TargetAssemblyIsNotPublicized = new(
            "HookGen0003",
            "Referenced type is in an assembly that is not publicized",
            "The referenced assembly '{0}' does not appear publicized. Set Publicize=\"true\" metadata on the reference item, " +
                "or add the <Publicize Include=\"{0}\"/> item to your csproj.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
            = ImmutableArray.Create<DiagnosticDescriptor>(
                ProjectDoesNotReferenceRuntimeDetour,
                ReferencedTypeIsInThisAssembly,
                TargetAssemblyIsNotPublicized
            );

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(context => {
                var compilation = context.Compilation;

                var attributeType = compilation.GetTypeByMetadataName(HookHelperGenerator.GenHelperForTypeAttributeFqn);
                if (attributeType is null) {
                    // somehow the generator isn't running, so we don't have anything to analyze
                    return;
                }

                var assembliesToReport = new ConcurrentDictionary<IAssemblySymbol, int>(SymbolEqualityComparer.Default);

                context.RegisterCompilationEndAction(context => {
                    if (assembliesToReport.IsEmpty) {
                        // no types were referenced, we shouldn't report this analyzer
                        return;
                    }

                    if (!context.Compilation.ReferencedAssemblyNames.Any(id => id.Name == "MonoMod.RuntimeDetour")) {
                        context.ReportDiagnostic(Diagnostic.Create(ProjectDoesNotReferenceRuntimeDetour, null, context.Compilation.AssemblyName));
                    }
                });

                context.RegisterOperationAction(context => {
                    var attribute = (IAttributeOperation) context.Operation;

                    if (attribute.Operation is not IObjectCreationOperation creationOp) {
                        // the attribute is invalid, don't touch it
                        return;
                    }

                    if (creationOp.Constructor is not { } ctor) {
                        // no constructor? invalid attribute maybe?
                        return;
                    }

                    var attrType = ctor.ContainingType;
                    if (!SymbolEqualityComparer.Default.Equals(attributeType, attrType)) {
                        // the attribute is not the one we care about
                        return;
                    }

                    // now lets try to process the arguments

                    if (creationOp.Arguments is not [var argOp]) {
                        // no argument, bad attribute
                        return;
                    }

                    if (argOp.Value is not ITypeOfOperation typeofOp) {
                        // not a typeof, invalid
                        return;
                    }

                    var targetType = typeofOp.TypeOperand;
                    var targetAssembly = targetType.ContainingAssembly;

                    if (SymbolEqualityComparer.Default.Equals(targetAssembly, compilation.Assembly)) {
                        // type is declared in this compilation
                        context.ReportDiagnostic(Diagnostic.Create(ReferencedTypeIsInThisAssembly, typeofOp.Syntax.GetLocation(), targetType));
                        // we shouldn't report any other diagnostics for this attribute, break out
                        return;
                    }

                    // queue the assemblies to be checked for publicisation
                    if (assembliesToReport.TryAdd(targetAssembly, 0)) {
                        // we are the first to get to this assembly, analyzer it

                        // look for BepInEx.AssemblyPublicizer's marker attribute in the assembly
                        var markerAttribute = targetAssembly.GetTypeByMetadataName("BepInEx.AssemblyPublicizer.OriginalAttributesAttribute");
                        if (markerAttribute is null) {
                            // the attribute is not present, this assembly does not look publicized
                            context.ReportDiagnostic(Diagnostic.Create(TargetAssemblyIsNotPublicized, typeofOp.Syntax.GetLocation(), targetAssembly.Identity.Name));
                        }
                    }

                    // TODO: report diagnostics for when the attribute will not cause anything to be generated?

                }, OperationKind.Attribute);
            });
        }
    }
}
