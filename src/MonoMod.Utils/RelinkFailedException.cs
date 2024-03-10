using Mono.Cecil;
using System;
using System.Text;

namespace MonoMod.Utils
{
    [Serializable]
    public class RelinkFailedException : Exception
    {

        public const string DefaultMessage = "MonoMod failed relinking";

        public IMetadataTokenProvider MTP { get; }
        public IMetadataTokenProvider? Context { get; }

        public RelinkFailedException(IMetadataTokenProvider mtp, IMetadataTokenProvider? context = null)
            : this(Format(DefaultMessage, mtp, context), mtp, context)
        {
        }

        public RelinkFailedException(string message,
            IMetadataTokenProvider mtp, IMetadataTokenProvider? context = null)
            : base(message)
        {
            MTP = mtp;
            Context = context;
        }

        public RelinkFailedException(string message, Exception innerException,
            IMetadataTokenProvider mtp, IMetadataTokenProvider? context = null)
            : base(message ?? Format(DefaultMessage, mtp, context), innerException)
        {
            MTP = mtp;
            Context = context;
        }

        protected static string Format(string message,
            IMetadataTokenProvider mtp, IMetadataTokenProvider? context)
        {
            if (mtp == null && context == null)
                return message;

            var builder = new StringBuilder(message);
            builder.Append(' ');

            if (mtp != null)
                builder.Append(mtp.ToString());

            if (context != null)
                builder.Append(' ');

            if (context != null)
                builder.Append("(context: ").Append(context.ToString()).Append(')');

            return builder.ToString();
        }
    }
}
