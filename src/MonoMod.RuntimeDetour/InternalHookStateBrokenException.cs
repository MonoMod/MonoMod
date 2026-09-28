using System;

namespace MonoMod.RuntimeDetour;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public",
    Justification = "This is a silly control-flow exception we use when something goes horribly wrong and need to full bail in a way which isn't completely catastrophic.")]
internal sealed class InternalHookStateBrokenException : Exception
{
    public InternalHookStateBrokenException()
    {
    }

    public InternalHookStateBrokenException(string message) : base(message)
    {
    }

    public InternalHookStateBrokenException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
