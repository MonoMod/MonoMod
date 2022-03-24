using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core {
    public readonly struct CoreDetour : IDisposable {

        public MethodBase Source => DetourImpl.Source;
        public MethodBase Destination => DetourImpl.Destination;

        internal ICoreDetour DetourImpl { get; }

        internal CoreDetour(ICoreDetour detourImpl)
            => DetourImpl = detourImpl;

        public void Apply() => DetourImpl?.Apply();
        public void Undo() => DetourImpl?.Undo();

        public void Dispose() {
            if (DetourImpl is { } impl) {
                impl.Undo();
                impl.Free();
            }
        }
    }
}
