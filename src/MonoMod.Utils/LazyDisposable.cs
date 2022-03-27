using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Utils {
    public sealed class LazyDisposable : IDisposable {
        public event Action OnDispose;

        public LazyDisposable() {
        }
        public LazyDisposable(Action a)
            : this() {
            OnDispose += a;
        }

        public void Dispose() {
            OnDispose?.Invoke();
        }
    }

    public sealed class LazyDisposable<T> : IDisposable {
        private T Instance;
        public event Action<T> OnDispose;

        public LazyDisposable(T instance) {
            Instance = instance;
        }
        public LazyDisposable(T instance, Action<T> a)
            : this(instance) {
            OnDispose += a;
        }

        public void Dispose() {
            OnDispose?.Invoke(Instance);
            Instance = default;
        }
    }
}
