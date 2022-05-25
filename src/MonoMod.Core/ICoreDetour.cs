using System;
using System.Reflection;

namespace MonoMod.Core {
    [CLSCompliant(true)]
    public interface ICoreDetour : IDisposable {

        MethodBase Source { get; }
        MethodBase Target { get; }

        bool IsApplied { get; }
        bool IsAttached { get; }

        void Apply();
        void Undo();

        void Detatch();
        void Attach();
    }
}
