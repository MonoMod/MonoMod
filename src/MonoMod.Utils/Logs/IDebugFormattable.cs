using System;

namespace MonoMod.Logs
{
    public interface IDebugFormattable
    {
        bool TryFormatInto(Span<char> span, out int wrote);
    }
}