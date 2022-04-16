using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms.Abi {
    public abstract class AbiBase : IAbi {
        public abstract ReadOnlyMemory<SpecialArgumentKind> ArgumentOrder { get; }

        public TypeClassification Classify(Type type, bool isReturn) {
            Helpers.ThrowIfNull(type);

            if (!type.IsValueType)
                return TypeClassification.Register;
            if (type.IsPrimitive)
                return TypeClassification.Register;
            if (type.IsPointer)
                return TypeClassification.Register;
            if (type.IsByRef)
                return TypeClassification.Register;

            // otherwise, call into the core classifier
            return ClassifyCore(type, isReturn);
        }

        protected abstract TypeClassification ClassifyCore(Type type, bool isReturn);
    }
}
