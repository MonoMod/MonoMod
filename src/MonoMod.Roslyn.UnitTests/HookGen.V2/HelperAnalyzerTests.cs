using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;
using MonoMod.Roslyn.UnitTests.Verifiers;
using MonoMod.HookGen.V2;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace MonoMod.Roslyn.UnitTests.HookGen.V2 {
#pragma warning disable IDE0065 // Misplaced using directive
    using VerifyCS = CSharpAnalyzerVerifier<MonoMod.HookGen.V2.HookHelperAnalyzer>;
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
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference
                    },
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task WarnOnRefTypeFromThisAssembly() {
            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers({|HookGen0002:typeof(ThisAsmType)|})]
                        
                        internal class ThisAsmType {
                            public int Get() { return 0; }
                        }
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference
                    },
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task ErrorOnNotPublicized() {
            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers({|HookGen0003:typeof(MonoMod.Roslyn.UnitTests.HookGen.V2.HelperGeneratorTests.TestClass)|})]
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference
                    },
                    ExpectedDiagnostics = {

                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task ErrorOnDoesNotReferenceRuntimeDetourAndNotPublicized() {
            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers({|HookGen0003:typeof(MonoMod.Roslyn.UnitTests.HookGen.V2.HelperGeneratorTests.TestClass)|})]
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        //HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference
                    },
                    ExpectedDiagnostics = {
                        VerifyCS.Diagnostic(HookHelperAnalyzer.ProjectDoesNotReferenceRuntimeDetour).WithArguments("TestProject"),
                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task WarnOnNotPublicized() {
            var coreReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(null, default).ConfigureAwait(false);
            var referenced = CSharpCompilation.Create("Referenced",
                syntaxTrees: [SyntaxFactory.ParseSyntaxTree("""
                    namespace Referenced;

                    public static class DetourClass {
                        public static void DoSomething() { }
                    }
                    """)],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilationMr = referenced.ToMetadataReference();

            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers({|HookGen0003:typeof(Referenced.DetourClass)|})]
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference,
                        compilationMr,
                    },
                    ExpectedDiagnostics = {

                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task DoNotWarnOnPublicized() {
            var coreReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(null, default).ConfigureAwait(false);
            var referenced = CSharpCompilation.Create("Referenced",
                syntaxTrees: [SyntaxFactory.ParseSyntaxTree("""
                    namespace BepInEx.AssemblyPublicizer {
                        internal sealed class OriginalAttributesAttribute : System.Attribute { }
                    }

                    namespace Referenced;

                    public static class DetourClass {
                        public static void DoSomething() { }
                    }
                    """)],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilationMr = referenced.ToMetadataReference();

            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers(typeof(Referenced.DetourClass))]
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference,
                        compilationMr,
                    },
                    ExpectedDiagnostics = {

                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task ErrorOnIrregularTypes() {
            var coreReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(null, default).ConfigureAwait(false);
            var referenced = CSharpCompilation.Create("Referenced",
                syntaxTrees: [SyntaxFactory.ParseSyntaxTree("""
                    namespace BepInEx.AssemblyPublicizer {
                        internal sealed class OriginalAttributesAttribute : System.Attribute { }
                    }

                    namespace Referenced;

                    public struct DetourClass {
                        public static void DoSomething() { }
                    }
                    """)],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilationMr = referenced.ToMetadataReference();

            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers({|HookGen0004:typeof(Referenced.DetourClass*)|})]
                        [assembly: GenerateHookHelpers({|HookGen0004:typeof(Referenced.DetourClass[])|})]
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference,
                        compilationMr,
                    },
                    ExpectedDiagnostics = {

                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task ErrorOnGenericType() {
            var coreReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(null, default).ConfigureAwait(false);
            var referenced = CSharpCompilation.Create("Referenced",
                syntaxTrees: [SyntaxFactory.ParseSyntaxTree("""
                    namespace BepInEx.AssemblyPublicizer {
                        internal sealed class OriginalAttributesAttribute : System.Attribute { }
                    }

                    namespace Referenced;

                    public struct DetourClass<T> {
                        public static void DoSomething() { }
                    }
                    """)],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilationMr = referenced.ToMetadataReference();

            await new Test {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                TestState = {
                    Sources = {
                        HookHelperGenerator.GenHelperForTypeAttributeSource,
                        """
                        using MonoMod.HookGen;
                        
                        [assembly: GenerateHookHelpers({|HookGen0005:typeof(Referenced.DetourClass<>)|})]
                        [assembly: GenerateHookHelpers({|HookGen0005:typeof(Referenced.DetourClass<int>)|})]
                        """
                    },
                    AdditionalReferences = {
                        HelperGeneratorTests.SelfMetadataReference,
                        HelperGeneratorTests.RuntimeDetourMetadataReference,
                        HelperGeneratorTests.UtilsMetadataReference,
                        compilationMr,
                    },
                    ExpectedDiagnostics = {

                    }
                }
            }.RunAsync().ConfigureAwait(false);
        }
    }
}
