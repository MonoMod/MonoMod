using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace MonoMod.HookGen.V2 {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class HookHelperAnalyzer : DiagnosticAnalyzer {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => throw new NotImplementedException();

        public override void Initialize(AnalysisContext context) {
            throw new NotImplementedException();
        }
    }
}
