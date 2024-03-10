using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Utils
{
    [SuppressMessage("Usage", "CA2237:Mark ISerializable types with serializable",
        Justification = "We don't really care about serialization for this exception.")]
    public sealed class AssertionFailedException : Exception
    {
        private const string AssertFailed = "Assertion failed! ";

        public AssertionFailedException() : base()
        {
            Message = "";
        }

        public AssertionFailedException(string? message) : base(AssertFailed + message)
        {
            Message = message ?? "";
        }

        public AssertionFailedException(string? message, Exception innerException) : base(AssertFailed + message, innerException)
        {
            Message = message ?? "";
        }

        public AssertionFailedException(string? message, string expression) : base($"{AssertFailed}{expression} {message}")
        {
            Message = message ?? "";
            Expression = expression;
        }

        public string Expression { get; } = "";

        public new string Message { get; }
    }
}
