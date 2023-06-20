using AsmResolver.DotNet;

namespace MonoMod.Packer {
    public interface IDiagnosticReciever {
        void ReportDiagnostic(string message, IMetadataMember? member, object?[] args);
    }
}
