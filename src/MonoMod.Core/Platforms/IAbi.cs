using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms {
    public interface IAbi {
        ReadOnlyMemory<SpecialArgumentKind> ArgumentOrder { get; }
        TypeClassification Classify(Type type, bool isReturn);
    }

    public enum TypeClassification {
        Register,
        PointerToMemory,
    }

    public enum SpecialArgumentKind {
        ThisPointer,
        ReturnBuffer,
        // TODO: should GenericContext be another member of this enum?
        UserArguments, // yes, this is needed. On x86, the generic context goes AFTER the user arguments.
    }
}
