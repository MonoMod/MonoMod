using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils {
    public sealed class DynData<TTarget> : IDisposable where TTarget : class {

        public static readonly HashSet<string> Disposable = new HashSet<string>();
        public static event Action<DynData<TTarget>, TTarget> OnInitialize;

        private static readonly _Data_ _DataStatic = new _Data_();
        private static readonly Dictionary<WeakReference, _Data_> _DataMap = new Dictionary<WeakReference, _Data_>(new WeakReferenceComparer());
        private static readonly Dictionary<string, Func<TTarget, object>> _SpecialGetters = new Dictionary<string, Func<TTarget, object>>();
        private static readonly Dictionary<string, Action<TTarget, object>> _SpecialSetters = new Dictionary<string, Action<TTarget, object>>();

        private readonly WeakReference Weak;
        private readonly _Data_ _Data;

        private class _Data_ {
            public readonly Dictionary<string, Func<TTarget, object>> Getters = new Dictionary<string, Func<TTarget, object>>();
            public readonly Dictionary<string, Action<TTarget, object>> Setters = new Dictionary<string, Action<TTarget, object>>();
            public readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        }

        public Dictionary<string, Func<TTarget, object>> Getters => _Data.Getters;
        public Dictionary<string, Action<TTarget, object>> Setters => _Data.Setters;
        public Dictionary<string, object> Data => _Data.Data;

        static DynData() {
            _DataHelper_.Collected += () => {
                HashSet<WeakReference> dead = new HashSet<WeakReference>();

                foreach (WeakReference weak in _DataMap.Keys)
                    if (!weak.IsAlive)
                        dead.Add(weak);

                foreach (WeakReference weak in dead)
                    _DataMap.Remove(weak);
            };

            // TODO: Use DynamicMethod to generate more performant getters and setters.
            foreach (FieldInfo field in typeof(TTarget).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string name = field.Name;
                _SpecialGetters[name] = (obj) => field.GetValue(obj);
                _SpecialSetters[name] = (obj, value) => field.SetValue(obj, value);
            }

            foreach (PropertyInfo prop in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string name = prop.Name;

                MethodInfo get = prop.GetGetMethod(true);
                if (get != null) {
                    FastReflectionDelegate getFast = get.CreateFastDelegate(true);
                    _SpecialGetters[name] = (obj) => getFast(obj);
                }

                MethodInfo set = prop.GetSetMethod(true);
                if (set != null) {
                    FastReflectionDelegate setFast = set.CreateFastDelegate(true);
                    _SpecialSetters[name] = (obj, value) => setFast(obj, value);
                }
            }
        }

        public bool IsAlive => Weak.IsAlive;
        public TTarget Target => Weak.Target as TTarget;

        public object this[string name] {
            get {
                if (_SpecialGetters.TryGetValue(name, out Func<TTarget, object> cb) ||
                    Getters.TryGetValue(name, out cb))
                    return cb(Weak.Target as TTarget);

                if (Data.TryGetValue(name, out object value))
                    return value;

                return null;
            }
            set {
                if (_SpecialSetters.TryGetValue(name, out Action<TTarget, object> cb) ||
                    Setters.TryGetValue(name, out cb)) {
                    cb(Weak.Target as TTarget, value);
                    return;
                }

                object prev;
                if (Disposable.Contains(name) && (prev = this[name]) != null && prev is IDisposable prevDisposable)
                    prevDisposable.Dispose();
                Data[name] = value;
            }
        }

        public DynData(TTarget obj) {
            if (obj != null) {
                WeakReference weak = new WeakReference(obj);
                lock (_DataMap) {
                    if (!_DataMap.TryGetValue(weak, out _Data)) {
                        _Data = new _Data_();
                        _DataMap.Add(weak, _Data);
                    }
                }
                Weak = weak;

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
            foreach (string name in Disposable)
                if (Data.TryGetValue(name, out object value) && value is IDisposable valueDisposable)
                    valueDisposable.Dispose();
            Data.Clear();
        }

        ~DynData() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private sealed class WeakReferenceComparer : EqualityComparer<WeakReference> {
            public override bool Equals(WeakReference x, WeakReference y)
                => ReferenceEquals(x.Target, y.Target) && x.IsAlive == y.IsAlive;

            public override int GetHashCode(WeakReference obj)
                => obj.Target?.GetHashCode() ?? 0;
        }
    }

    internal static class _DataHelper_ {
        public static event Action Collected;
        static _DataHelper_() {
            new CollectionDummy();
        }
        private sealed class CollectionDummy {
            ~CollectionDummy() {
                GC.ReRegisterForFinalize(this);
                Collected?.Invoke();
            }
        }
    }
}
