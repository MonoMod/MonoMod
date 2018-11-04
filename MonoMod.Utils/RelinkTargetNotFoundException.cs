using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace MonoMod.Utils {
    [MonoMod__OldName__("MonoMod.RelinkTargetNotFoundException")]
    public class RelinkTargetNotFoundException : RelinkFailedException {

        public new const string DefaultMessage = "MonoMod relinker failed finding";

        public RelinkTargetNotFoundException(IMetadataTokenProvider mtp, IMetadataTokenProvider context = null)
            : base(_Format(DefaultMessage, mtp, context), mtp, context) {
        }

        public RelinkTargetNotFoundException(string message,
            IMetadataTokenProvider mtp, IMetadataTokenProvider context = null)
            : base(message ?? DefaultMessage, mtp, context) {
        }

        public RelinkTargetNotFoundException(string message, Exception innerException,
            IMetadataTokenProvider mtp, IMetadataTokenProvider context = null)
            : base(message ?? DefaultMessage, innerException, mtp, context) {
        }

    }
}
