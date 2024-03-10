using Mono.Cecil;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        public static IMetadataTokenProvider ImportReference(this ModuleDefinition mod, IMetadataTokenProvider mtp)
        {
            Helpers.ThrowIfArgumentNull(mod);
            if (mtp is TypeReference type)
                return mod.ImportReference(type);
            if (mtp is FieldReference field)
                return mod.ImportReference(field);
            if (mtp is MethodReference method)
                return mod.ImportReference(method);
            return mtp;
        }

    }
}
