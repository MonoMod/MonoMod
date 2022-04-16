using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MonoMod.Utils;

namespace MonoMod.Core.Platforms.Abi {
    public class WinX64NativeAbi : AbiBase {
        public override ReadOnlyMemory<SpecialArgumentKind> ArgumentOrder { get; } = new[] { 
            SpecialArgumentKind.ReturnBuffer,
            SpecialArgumentKind.ThisPointer,
            SpecialArgumentKind.UserArguments,
        };

        protected override TypeClassification ClassifyCore(Type type, bool isReturn) {
            var size = type.GetManagedSize();
            if (size is 1 or 2 or 4 or 8) {
                return TypeClassification.Register;
            } else {
                return TypeClassification.PointerToMemory;
            }
        }
    }
}
