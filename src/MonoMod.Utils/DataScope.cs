using System;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Utils
{
    public abstract class ScopeHandlerBase
    {
        public abstract void EndScope(object? data);
    }

    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types",
        Justification = "These types are not meant to be compared.")]
    public readonly struct DataScope : IDisposable
    {
        private readonly ScopeHandlerBase? handler;
        private readonly object? data;
        public object? Data => data;

        public DataScope(ScopeHandlerBase handler, object? data)
        {
            this.handler = handler;
            this.data = data;
        }

        public void Dispose()
        {
            if (handler is not null)
            {
                handler.EndScope(data);
            }
        }
    }

    public abstract class ScopeHandlerBase<T> : ScopeHandlerBase
    {
        public sealed override void EndScope(object? data) => EndScope((T)data!);
        public abstract void EndScope(T data);
    }

    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types",
        Justification = "These types are not meant to be compared.")]
    public readonly struct DataScope<T> : IDisposable
    {
        private readonly ScopeHandlerBase<T>? handler;
        private readonly T data;
        public T Data => data;

        public DataScope(ScopeHandlerBase<T> handler, T data)
        {
            this.handler = handler;
            this.data = data;
        }

        public void Dispose()
        {
            if (handler is not null)
            {
                handler.EndScope(data);
            }
        }
    }
}
