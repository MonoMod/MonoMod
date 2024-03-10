#nullable enable
using Mono.Cecil;

namespace MonoMod
{
    internal static class MultiTargetShims
    {

#if CECIL0_10
    public static TypeReference GetConstraintType(this TypeReference type)
        => type;
#else
        public static TypeReference GetConstraintType(this GenericParameterConstraint constraint)
            => constraint.ConstraintType;
#endif

    }
}