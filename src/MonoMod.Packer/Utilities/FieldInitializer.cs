using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace MonoMod.Packer.Utilities {
    internal sealed class FieldInitializer : IEquatable<FieldInitializer>, IEqualityComparer<(CilOpCode, object?)> {
        public readonly TypeEntityMap Map;
        public ImmutableArray<(CilOpCode OpCode, object? Operand)> Instructions { get; }

        public FieldInitializer(TypeEntityMap map, ImmutableArray<(CilOpCode, object?)> insns) {
            Map = map;
            Instructions = insns;
        }

        public bool Equals([NotNullWhen(true)] FieldInitializer? other) {
            if (other is null)
                return false;

            return Instructions.SequenceEqual(other.Instructions, this);
        }

        bool IEqualityComparer<(CilOpCode, object?)>.Equals((CilOpCode, object?) x, (CilOpCode, object?) y) {
            return x.Item1 == y.Item1 && ObjEquals(x.Item2, y.Item2);
        }

        int IEqualityComparer<(CilOpCode, object?)>.GetHashCode((CilOpCode, object?) obj) {
            return HashCode.Combine(obj.Item1, GetObjHashCode(obj.Item2));
        }

        // TODO: use a (custom) SignatureComparer for operands, when appropriate

        public override bool Equals([NotNullWhen(true)] object? obj) {
            return obj is FieldInitializer init && Equals(init);
        }

        public override int GetHashCode() {
            var hc = new HashCode();
            foreach (var (op, obj) in Instructions) {
                hc.Add(op);
                hc.Add(GetObjHashCode(obj));
            }
            return hc.ToHashCode();
        }

        private bool ObjEquals(object? obj, object? other) {
            return obj switch {
                null => other is null,
                byte[] x => Map.UnifiedComparer.Equals(x, other as byte[]),
                ITypeDescriptor x => Map.UnifiedComparer.Equals(x, other as ITypeDescriptor),
                IEnumerable<TypeSignature> x => Map.UnifiedComparer.Equals(x, other as IEnumerable<TypeSignature>),
                MemberReference x => x.IsMethod
                    ? Map.UnifiedComparer.Equals(x, other as IMethodDescriptor)
                    : Map.UnifiedComparer.Equals(x, other as IFieldDescriptor),
                IMethodDescriptor x => Map.UnifiedComparer.Equals(x, other as IMethodDescriptor),
                IFieldDescriptor x => Map.UnifiedComparer.Equals(x, other as IFieldDescriptor),
                var x => x.Equals(other)
            };
        }

        private int GetObjHashCode(object? obj) {
            return obj switch {
                null => 0,
                byte[] x => Map.UnifiedComparer.GetHashCode(x),
                TypeSignature x => Map.UnifiedComparer.GetHashCode(x),
                IList<TypeSignature> x => Map.UnifiedComparer.GetHashCode(x),
                IEnumerable<TypeSignature> x => Map.UnifiedComparer.GetHashCode(x),
                TypeDefinition x => Map.UnifiedComparer.GetHashCode(x),
                TypeReference x => Map.UnifiedComparer.GetHashCode(x),
                TypeSpecification x => Map.UnifiedComparer.GetHashCode(x),
                ExportedType x => Map.UnifiedComparer.GetHashCode(x),
                ITypeDefOrRef x => Map.UnifiedComparer.GetHashCode(x),
                ITypeDescriptor x => Map.UnifiedComparer.GetHashCode(x),
                MethodSpecification x => Map.UnifiedComparer.GetHashCode(x),
                MemberReference x => Map.UnifiedComparer.GetHashCode(x),
                IMethodDescriptor x => Map.UnifiedComparer.GetHashCode(x),
                IFieldDescriptor x => Map.UnifiedComparer.GetHashCode(x),
                CallingConventionSignature x => Map.UnifiedComparer.GetHashCode(x),
                var x => x.GetHashCode(),
            };
        }

        public static bool operator ==(FieldInitializer? l, FieldInitializer? r)
            => (l is null && r is null)
            || l is not null && l.Equals(r);

        public static bool operator !=(FieldInitializer? l, FieldInitializer? r)
            => !(l == r);
    }
}
