using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Packer.Utilities {
    internal sealed class TypeLayoutEqualityComparer : IEqualityComparer<TypeDefinition> {
        private readonly SignatureComparer sigComparer;

        public static readonly TypeLayoutEqualityComparer Default = new(new());

        public TypeLayoutEqualityComparer(SignatureComparer sigComparer) {
            this.sigComparer = sigComparer;
        }

        public bool Equals(TypeDefinition? x, TypeDefinition? y) {
            if (x is null && y is null)
                return true;
            if (x is null || y is null)
                return false;

            if ((x.Attributes & TypeAttributes.LayoutMask) != (y.Attributes & TypeAttributes.LayoutMask))
                return false;

            if (x.ClassLayout is { } xlayout) {
                if (y.ClassLayout is not { } ylayout)
                    return false;

                if (xlayout.ClassSize != ylayout.ClassSize
                    || xlayout.PackingSize != ylayout.PackingSize)
                    return false;
            } else {
                if (y.ClassLayout is not null)
                    return false;
            }

            int xi = 0, yi = 0;

            while (MoveNextField(x, ref xi, out var xf)) {
                if (!MoveNextField(y, ref yi, out var yf))
                    return false;

                if (!sigComparer.Equals(xf.Signature, yf.Signature))
                    return false;
            }

            if (MoveNextField(y, ref yi, out _))
                return false;

            return true;
        }

        private static bool MoveNextField(TypeDefinition type, ref int idx, [MaybeNullWhen(false)] out FieldDefinition field) {
            var nextIdx = idx;
            Helpers.DAssert(nextIdx >= 0);
            while (nextIdx < type.Fields.Count) {
                field = type.Fields[nextIdx++];
                if (!field.IsStatic) {
                    idx = nextIdx;
                    return true;
                }
            }
            idx = nextIdx;
            field = null;
            return false;
        }

        public int GetHashCode([DisallowNull] TypeDefinition obj) {
            var hc = new HashCode();

            hc.Add(obj.Attributes & TypeAttributes.LayoutMask);
            if (obj.ClassLayout is { } layout) {
                hc.Add(layout.ClassSize);
                hc.Add(layout.PackingSize);
            }

            foreach (var field in obj.Fields) {
                if (field.IsStatic)
                    continue;

                hc.Add(field.Signature is { } sig ? sigComparer.GetHashCode(sig) : 0);
            }

            return hc.ToHashCode();
        }
    }
}
