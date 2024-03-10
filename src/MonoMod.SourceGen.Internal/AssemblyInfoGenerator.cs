using Microsoft.CodeAnalysis;
using System.Text;

namespace MonoMod.SourceGen.Internal
{
    // The purpose of this generator is to expose certain build-time properties, like the assembly name and version,
    // so we don't need reflection to access this information.
    [Generator]
    public class AssemblyInfoGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        private static readonly DiagnosticDescriptor WRN_CouldNotGetAssemblyName = new(
            "MM0001",
            "Could not get assembly name to write in assembly info",
            "Could not get assembly name to write in assembly info, using MonoMod.UnknownAssemblyName instead",
            "Build",
            DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor WRN_CouldNotGetVersion = new(
            "MM0002",
            "Could not get version to write in assembly info",
            "Could not get version to write in assembly info",
            "Build",
            DiagnosticSeverity.Warning, true);

        private const string AssemblyInfoClass = "AssemblyInfo";

        public void Execute(GeneratorExecutionContext context)
        {

            var asmName = context.Compilation.AssemblyName;
            if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.AssemblyName", out var asmNameProp))
            {
                asmName = asmNameProp;
            }
            if (asmName is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(WRN_CouldNotGetAssemblyName, null));
                asmName = "MonoMod.UnknownAssemblyName";
            }

            if (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.Version", out var version))
            {
                context.ReportDiagnostic(Diagnostic.Create(WRN_CouldNotGetVersion, null));
                version = "unknown";
            }

            if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace))
            {
                rootNamespace = rootNamespace.Trim();
                if (string.IsNullOrWhiteSpace(rootNamespace))
                {
                    rootNamespace = null;
                }
            }

            // we've gathered all the information we need to write our source file
            var sb = new StringBuilder();
            var cb = new CodeBuilder(sb);
            _ = cb.WriteHeader();

            if (rootNamespace is not null)
            {
                _ = cb // TODO: should this even go in the root namespace of the project? or should it go in MonoMod?
                    .Write("global using AssemblyInfo = ").Write(rootNamespace).Write('.').Write(AssemblyInfoClass).WriteLine(';')
                    .Write("namespace ").Write(rootNamespace).WriteLine(" {").IncreaseIndent();
            }

            _ = cb.WriteLine("internal static partial class AssemblyInfo {").IncreaseIndent();
            _ = cb
                .Write("public const string AssemblyName = \"").Write(asmName).WriteLine("\";")
                .Write("public const string AssemblyVersion = \"").Write(version).WriteLine("\";");
            _ = cb.DecreaseIndent().WriteLine("}");

            if (rootNamespace is not null)
            {
                _ = cb.DecreaseIndent().WriteLine("}");
            }

            context.AddSource("AssemblyInfo.g.cs", sb.ToString());
        }
    }
}
