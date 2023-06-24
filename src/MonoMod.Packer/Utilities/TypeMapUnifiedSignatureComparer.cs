using AsmResolver.DotNet;

namespace MonoMod.Packer.Utilities {
    internal class TypeMapUnifiedSignatureComparer : SignatureComparer {
        private readonly TypeEntityMap map;
        public TypeMapUnifiedSignatureComparer(TypeEntityMap map) {
            this.map = map;
        }

        // TODO: compare types instead by their materialized versions
        protected override bool SimpleTypeEquals(ITypeDescriptor x, ITypeDescriptor y) {
            if (x is ITypeDefOrRef tdor) {
                if (y is not ITypeDefOrRef tdory) {
                    return false;
                }

                var xe = map.GetEntity(tdor).GetUnified();
                var ye = map.GetEntity(tdory).GetUnified();
                return xe == ye;
            }

            return base.SimpleTypeEquals(x, y);
        }

        protected override int SimpleTypeHashCode(ITypeDescriptor obj) {
            if (obj is ITypeDefOrRef tdor) {
                var ent = map.GetEntity(tdor).GetUnified();
                return ent.GetHashCode();
            }
            return base.SimpleTypeHashCode(obj);
        }
    }
}
