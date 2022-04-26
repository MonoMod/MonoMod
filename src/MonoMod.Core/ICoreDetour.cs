using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core {
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
