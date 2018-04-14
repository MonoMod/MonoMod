using Mono.Cecil;
using MonoMod.Utils;
using System;

namespace MonoMod.InlineRT {
    internal class MonoModRulesModder : MonoModder {

        public TypeDefinition Orig;

        public override void Log(string text) {
            Console.Write("[MonoMod] [RulesModder] ");
            Console.WriteLine(text);
        }

        public override IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            if (mtp is TypeReference && ((TypeReference) mtp).FullName == Orig.FullName) {
                return Module.GetType(Orig.FullName);
            }
            return base.Relinker(mtp, context);
        }

    }
}
