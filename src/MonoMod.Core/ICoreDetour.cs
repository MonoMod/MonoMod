using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core {
    public interface ICoreDetour {

        MethodBase Source { get; }
        MethodBase Destination { get; }

        void Apply();
        void Undo();
        void Free();
    }
}
