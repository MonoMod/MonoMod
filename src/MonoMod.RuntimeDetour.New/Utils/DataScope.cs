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

    public abstract class ScopeHandlerBase<T> : ScopeHandlerBase {
        public sealed override void EndScope(object? data) => EndScope((T) data!);
        public abstract void EndScope(T data);
    }

    public struct DataScope<T> : IDisposable {
        private readonly ScopeHandlerBase<T>? handler;
        private readonly T data;

        public DataScope(ScopeHandlerBase<T> handler, T data) {
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
