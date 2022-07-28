using MonoMod.Core.Utils;
using System;

namespace MonoMod.Core.Platforms {
    public enum TypeClassification {
        InRegister,
        ByReference,
        OnStack
    }

    public delegate TypeClassification Classifier(Type type, bool isReturn);

    public enum SpecialArgumentKind {
        ThisPointer,
        ReturnBuffer,
        GenericContext,
        UserArguments, // yes, this is needed. On x86, the generic context goes AFTER the user arguments.
    }

    // TODO: include information about how many registers are used for parameter passing
    public readonly record struct Abi(
        ReadOnlyMemory<SpecialArgumentKind> ArgumentOrder,
        Classifier Classifier,
        bool ReturnsReturnBuffer
    ) {
        public TypeClassification Classify(Type type, bool isReturn) {
            Helpers.ThrowIfArgumentNull(type);

            if (type == typeof(void))
                return TypeClassification.InRegister; // void can't be a parameter, and doesn't need a return buffer
            if (!type.IsValueType)
                return TypeClassification.InRegister; // while it won't *always* go in register, it will if it can
            if (type.IsPointer)
                return TypeClassification.InRegister; // same as above
            if (type.IsByRef)
                return TypeClassification.InRegister; // same as above

            return Classifier(type, isReturn);
        }
    }
}
