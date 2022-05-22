using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.RuntimeDetour.Utils {

    public abstract class ScopeHandlerBase {
        public abstract void EndScope(object? data);
    }

    public struct DataScope : IDisposable {
        private readonly ScopeHandlerBase? handler;
        private readonly object? data;

        public DataScope(ScopeHandlerBase handler, object? data) {
            this.handler = handler;
            this.data = data;
        }

        public void Dispose() {
            if (handler is not null) {
                handler.EndScope(data);
            }
        }
    }
}
