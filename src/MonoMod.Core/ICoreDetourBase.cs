using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core {
    public interface ICoreDetourBase : IDisposable {
        bool IsApplied { get; }

        void Apply();
        void Undo();
    }
}
