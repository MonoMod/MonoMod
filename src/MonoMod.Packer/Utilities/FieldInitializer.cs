using AsmResolver.PE.DotNet.Cil;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace MonoMod.Packer.Utilities {
    internal sealed class FieldInitializer : IEquatable<FieldInitializer> {
        public ImmutableArray<(CilOpCode, object?)> Instructions { get; }

        public FieldInitializer(ImmutableArray<(CilOpCode, object?)> insns) {
            Instructions = insns;
        }

        public bool Equals([NotNullWhen(true)] FieldInitializer? other) {
            if (other is null)
                return false;

            return Instructions.SequenceEqual(other.Instructions);
        }

        public override bool Equals([NotNullWhen(true)] object? obj) {
            return Equals(obj as FieldInitializer);
        }

        public override int GetHashCode() {
            var hc = new HashCode();
            foreach (var (op, obj) in Instructions) {
                hc.Add(op);
                hc.Add(obj);
            }
            return hc.ToHashCode();
        }
    }
}
