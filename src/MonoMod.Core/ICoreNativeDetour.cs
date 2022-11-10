using System;

namespace MonoMod.Core {
    public interface ICoreNativeDetour {
        IntPtr From { get; }
        IntPtr To { get; }

        bool HasOrigEntrypoint { get; }
        IntPtr OrigEntrypoint { get; }

        void Undo();
    }
}
