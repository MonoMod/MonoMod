using Microsoft.CodeAnalysis;
using System;
using System.Text;

namespace MonoMod.SourceGen.Internal
{
    // The purpose of this generator is to expose certain build-time properties, like the assembly name and version,
    // so we don't need reflection to access this information.
    [Generator]
    public class AssemblyInfoGenerator : IIncrementalGenerator
    {
        private sealed record Properties(
            string? Version,
            string? RootNamespace
            );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var assemblyName = context
                .CompilationProvider
                .Select((c, ct) => c.AssemblyName);

            var otherProperties = context
                .AnalyzerConfigOptionsProvider
                .Select((options, ct)
                    => new Properties(
                        options.GlobalOptions.TryGetValue("build_property.Version", out var ver) ? ver : null,
                        options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace) ? rootNamespace : null
                        ));

            context.RegisterSourceOutput(assemblyName.Combine(otherProperties), (spc, t) =>
            {
                var (assemblyName, properties) = t;

                var sb = new StringBuilder();
                var cb = new CodeBuilder(sb);
                _ = cb.WriteHeader();

                if (properties.RootNamespace is not null)
                {
                    _ = cb // TODO: should this even go in the root namespace of the project? or should it go in MonoMod?
                        .Write("global using AssemblyInfo = ").Write(properties.RootNamespace).Write('.').WriteLine("AssemblyInfo;")
                        .Write("namespace ").Write(properties.RootNamespace).WriteLine(" {").IncreaseIndent();
                }

                _ = cb.WriteLine("internal static partial class AssemblyInfo {").IncreaseIndent();
                if (assemblyName is not null)
                {
                    _ = cb.Write("public const string AssemblyName = \"").Write(assemblyName).WriteLine("\";");
                }
                if (properties.Version is not null)
                {
                    _ = cb.Write("public const string AssemblyVersion = \"").Write(properties.Version).WriteLine("\";");
                }
                _ = cb.DecreaseIndent().WriteLine("}");

                if (properties.RootNamespace is not null)
                {
                    _ = cb.DecreaseIndent().WriteLine("}");
                }

                spc.AddSource("AssemblyInfo.g.cs", sb.ToString());
            });
        }
    }
}
