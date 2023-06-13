using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;

namespace MonoMod.Packer.Driver {
    internal sealed class ConsoleDiagnosticReciever : IDiagnosticReciever {
        private readonly IConsole console;
        private readonly InvocationContext context;

        public ConsoleDiagnosticReciever(IConsole console, InvocationContext context) {
            this.console = console;
            this.context = context;
        }

        public void ReportDiagnostic(string message, object?[] args) {
            context.ExitCode = -1;
            console.Out.WriteLine(message);
            foreach (var arg in args) {
                console.Out.WriteLine(arg?.ToString() ?? "<null>");
            }
        }
    }
}
