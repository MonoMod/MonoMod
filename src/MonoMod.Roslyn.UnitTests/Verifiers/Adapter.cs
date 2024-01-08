using Microsoft.CodeAnalysis;

namespace MonoMod.Roslyn.UnitTests.Verifiers {
    public class Adapter<TIncrementalGenerator> : ISourceGenerator, IIncrementalGenerator
        where TIncrementalGenerator : IIncrementalGenerator, new() {
        private readonly TIncrementalGenerator _internalGenerator = new();

        public void Execute(GeneratorExecutionContext context) {
            throw new System.NotImplementedException();
        }

        public void Initialize(GeneratorInitializationContext context) {
            throw new System.NotImplementedException();
        }

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            _internalGenerator.Initialize(context);
        }
    }
}
