using System;

namespace MonoMod.Core {
    public interface ICoreDetourBase : IDisposable {
        bool IsApplied { get; }

        void Apply();
        void Undo();
    }
}
