using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;
using MonoMod.Roslyn.UnitTests.Verifiers;
using MonoMod.HookGen.V2;

namespace MonoMod.Roslyn.UnitTests.HookGen.V2 {
#pragma warning disable IDE0065 // Misplaced using directive
    using Test = CSharpAnalyzerVerifier<MonoMod.HookGen.V2.HookHelperAnalyzer>.Test;
#pragma warning restore IDE0065 // Misplaced using directive

    public class HelperAnalyzerTests {

        [Fact]
        public async Task NoDiagnosticsWithNoAttributes() {
            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        // no body
                        """
                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }
    }
}
