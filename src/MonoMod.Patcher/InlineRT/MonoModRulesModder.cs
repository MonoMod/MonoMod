using Mono.Cecil;
using System;

namespace MonoMod.InlineRT
{
    internal sealed class MonoModRulesModder : MonoModder
    {

        public TypeDefinition Orig;

        public override void Log(string text)
        {
            Console.Write("[MonoMod] [RulesModder] ");
            Console.WriteLine(text);
        }

        public override IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context)
        {
            // Bypass the relinker for the MonoMod rules type + all its nested types
            if (mtp is TypeReference typeRef && Orig.Module.GetType(typeRef.FullName) is TypeDefinition origType)
                for (; origType != null; origType = origType.DeclaringType)
                    if (origType == Orig)
                        return Module.GetType(typeRef.FullName);

            return base.Relinker(mtp, context);
        }

    }
}
