namespace MonoMod.Packer {
    public interface IDiagnosticReciever {
        void ReportDiagnostic(string message, object?[] args);
    }
}
