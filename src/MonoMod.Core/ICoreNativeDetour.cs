using System;

namespace MonoMod.Core {
    public interface ICoreNativeDetour : ICoreDetourBase {
        IntPtr Source { get; }
        IntPtr Target { get; }

        bool HasOrigEntrypoint { get; }
        IntPtr OrigEntrypoint { get; }
    }
}
