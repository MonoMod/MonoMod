using System;
using Mono.Cecil;

namespace MonoMod.Utils {
    public class RelinkTargetNotFoundException : RelinkFailedException {

        private const string DefaultMessage = "MonoMod relinker failed finding";

        public RelinkTargetNotFoundException(IMetadataTokenProvider mtp, IMetadataTokenProvider? context = null)
            : base(Format(DefaultMessage, mtp, context), mtp, context) {
        }

        public RelinkTargetNotFoundException(string message,
            IMetadataTokenProvider mtp, IMetadataTokenProvider? context = null)
            : base(message ?? DefaultMessage, mtp, context) {
        }

        public RelinkTargetNotFoundException(string message, Exception innerException,
            IMetadataTokenProvider mtp, IMetadataTokenProvider? context = null)
            : base(message ?? DefaultMessage, innerException, mtp, context) {
        }

    }
}
