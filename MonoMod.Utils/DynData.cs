using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils {
    public sealed class DynData<TTarget> : IDisposable where TTarget : class {

        private static int CreationsInProgress = 0;

        private static readonly object[] _NoArgs = new object[0];

        public static event Action<DynData<TTarget>, TTarget> OnInitialize;

        private static readonly _Data_ _DataStatic = new _Data_();
#if !NETFRAMEWORK3
        private static readonly ConditionalWeakTable<object, _Data_> _DataMap = new ConditionalWeakTable<object, _Data_>();
#else
        private static readonly Dictionary<WeakReference, _Data_> _DataMap = new Dictionary<WeakReference, _Data_>(new WeakReferenceComparer());
        private static readonly HashSet<WeakReference> _DataMapDead = new HashSet<WeakReference>();
#endif
        private static readonly Dictionary<string, Func<TTarget, object>> _SpecialGetters = new Dictionary<string, Func<TTarget, object>>();
        private static readonly Dictionary<string, Action<TTarget, object>> _SpecialSetters = new Dictionary<string, Action<TTarget, object>>();

        private readonly WeakReference Weak;
        private TTarget KeepAlive;
        private readonly _Data_ _Data;

        private class _Data_ : IDisposable {
            public readonly Dictionary<string, Func<TTarget, object>> Getters = new Dictionary<string, Func<TTarget, object>>();
            public readonly Dictionary<string, Action<TTarget, object>> Setters = new Dictionary<string, Action<TTarget, object>>();
            public readonly Dictionary<string, object> Data = new Dictionary<string, object>();
            public readonly HashSet<string> Disposable = new HashSet<string>();

            ~_Data_() {
                Dispose();
            }

            public void Dispose() {
                lock (Data) {
                    if (Data.Count == 0)
                        return;

                    foreach (string name in Disposable)
                        if (Data.TryGetValue(name, out object value) && value is IDisposable valueDisposable)
                            valueDisposable.Dispose();
                    Disposable.Clear();

                    Data.Clear();
                }
            }
        }

        public Dictionary<string, Func<TTarget, object>> Getters => _Data.Getters;
        public Dictionary<string, Action<TTarget, object>> Setters => _Data.Setters;
        public Dictionary<string, object> Data => _Data.Data;

        static DynData() {
#if NETFRAMEWORK3
            GCListener.OnCollect += () => {
                if (CreationsInProgress != 0)
                    return;

                lock (_DataMap) {
                    foreach (KeyValuePair<WeakReference, _Data_> kvp in _DataMap) {
                        if (kvp.Key.SafeGetIsAlive())
                            continue;
                        _DataMapDead.Add(kvp.Key);
                        kvp.Value.Dispose();
                    }

                    foreach (WeakReference weak in _DataMapDead) {
                        _DataMap.Remove(weak);
                    }

                    _DataMapDead.Clear();
                }
            };
#endif

            foreach (FieldInfo field in typeof(TTarget).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string name = field.Name;
                _SpecialGetters[name] = (obj) => field.GetValue(obj);
                _SpecialSetters[name] = (obj, value) => field.SetValue(obj, value);
            }

            foreach (PropertyInfo prop in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string name = prop.Name;

                MethodInfo get = prop.GetGetMethod(true);
                if (get != null) {
                    _SpecialGetters[name] = (obj) => get.Invoke(obj, _NoArgs);
                }

                MethodInfo set = prop.GetSetMethod(true);
                if (set != null) {
                    _SpecialSetters[name] = (obj, value) => set.Invoke(obj, new object[] { value });
                }
            }
        }

        public bool IsAlive => Weak == null || Weak.SafeGetIsAlive();
        public TTarget Target => Weak?.SafeGetTarget() as TTarget;

        public object this[string name] {
            get {
                if (_SpecialGetters.TryGetValue(name, out Func<TTarget, object> cb) ||
                    Getters.TryGetValue(name, out cb))
                    return cb(Weak?.SafeGetTarget() as TTarget);

                if (Data.TryGetValue(name, out object value))
                    return value;

                return null;
            }
            set {
                if (_SpecialSetters.TryGetValue(name, out Action<TTarget, object> cb) ||
                    Setters.TryGetValue(name, out cb)) {
                    cb(Weak?.SafeGetTarget() as TTarget, value);
                    return;
                }

                object prev;
                if (_Data.Disposable.Contains(name) && (prev = this[name]) != null && prev is IDisposable prevDisposable)
                    prevDisposable.Dispose();
                Data[name] = value;
            }
        }

        public DynData()
            : this(null, false) {
        }

        public DynData(TTarget obj)
            : this(obj, true) {
        }

        public DynData(TTarget obj, bool keepAlive) {
            if (obj != null) {
                WeakReference weak = new WeakReference(obj);

#if NETFRAMEWORK3
                WeakReference key = weak;
#else
                object key = obj;
#endif

                // Ideally this would be a "no GC region", but that's too new.
                CreationsInProgress++;
                lock (_DataMap) {
                    if (!_DataMap.TryGetValue(key, out _Data)) {
                        _Data = new _Data_();
                        _DataMap.Add(key, _Data);
                    }
                }
                CreationsInProgress--;
                
                Weak = weak;
                if (keepAlive)
                    KeepAlive = obj;

            } else {
                _Data = _DataStatic;
            }

            OnInitialize?.Invoke(this, obj);
        }

        public T Get<T>(string name)
            => (T) this[name];

        public void Set<T>(string name, T value)
            => this[name] = value;

        public void RegisterProperty(string name, Func<TTarget, object> getter, Action<TTarget, object> setter) {
            Getters[name] = getter;
            Setters[name] = setter;
        }

        public void UnregisterProperty(string name) {
            Getters.Remove(name);
            Setters.Remove(name);
        }

        private void Dispose(bool disposing) {
            KeepAlive = default;
        }

        ~DynData() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

    }
}
