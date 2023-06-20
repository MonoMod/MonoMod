using AsmResolver.DotNet;
using MonoMod.Packer.Entities;
using System;

namespace MonoMod.Packer.Diagnostics {
    internal sealed class DiagnosticTranslator {
        private readonly IDiagnosticReciever reciever;

        public DiagnosticTranslator(IDiagnosticReciever reciever) {
            this.reciever = reciever;
        }

        public void ReportDiagnostic(ErrorCode code, object?[]? args = null) {
            // TODO: improve
            reciever.ReportDiagnostic(code.ToString(), null, args ?? Array.Empty<object?>());
        }

        public void ReportDiagnostic(ErrorCode code, IMetadataMember scope, object?[]? args = null) {
            // TODO: improve
            reciever.ReportDiagnostic(code.ToString(), scope, args ?? Array.Empty<object?>());
        }

        public void ReportDiagnostic(ErrorCode code, EntityBase entity, object?[]? args = null) {
            // TODO: improve
            reciever.ReportDiagnostic(code.ToString(), null, args ?? Array.Empty<object?>());
        }
    }
}
