using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils {
    public class _Data_<TTarget> : IDisposable where TTarget : class {

        public static readonly HashSet<string> Disposable = new HashSet<string>();
        public static readonly Dictionary<string, Func<TTarget, object>> GetterMap = new Dictionary<string, Func<TTarget, object>>();
        public static readonly Dictionary<string, Action<TTarget, object>> SetterMap = new Dictionary<string, Action<TTarget, object>>();
        public static event Action<_Data_<TTarget>, TTarget> OnInitialize;

        private static readonly Dictionary<WeakReference, _Data_<TTarget>> ObjectMap = new Dictionary<WeakReference, _Data_<TTarget>>(new WeakReferenceComparer());
        private readonly WeakReference Weak;
        private readonly Dictionary<string, object> VarMap = new Dictionary<string, object>();
        
        static _Data_() {
            _ExtraHelper_.Collected += () => {
                HashSet<WeakReference> dead = new HashSet<WeakReference>();

                foreach (WeakReference weak in ObjectMap.Keys)
                    if (!weak.IsAlive)
                        dead.Add(weak);

                foreach (WeakReference weak in dead)
                    ObjectMap.Remove(weak);
            };

            // TODO: Use DynamicMethod to generate more performant getters and setters.
            foreach (FieldInfo field in typeof(TTarget).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string name = field.Name;
                GetterMap[name] = (obj) => field.GetValue(obj);
                SetterMap[name] = (obj, value) => field.SetValue(obj, value);
            }

            foreach (PropertyInfo prop in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string name = prop.Name;

                MethodInfo get = prop.GetGetMethod(true);
                if (get != null) {
                    FastReflectionDelegate getFast = get.CreateFastDelegate(true);
                    GetterMap[name] = (obj) => getFast(obj);
                }

                MethodInfo set = prop.GetSetMethod(true);
                if (set != null) {
                    FastReflectionDelegate setFast = set.CreateFastDelegate(true);
                    SetterMap[name] = (obj, value) => setFast(obj, value);
                }
            }
        }

        public bool IsAlive => Weak.IsAlive;

        public object this[string name] {
            get {
                if (GetterMap.TryGetValue(name, out Func<TTarget, object> cb))
                    return cb(Weak.Target as TTarget);

                if (VarMap.TryGetValue(name, out object value))
                    return value;

                return null;
            }
            set {
                if (SetterMap.TryGetValue(name, out Action<TTarget, object> cb)) {
                    cb(Weak.Target as TTarget, value);
                    return;
                }

                object prev;
                if (Disposable.Contains(name) && (prev = this[name]) != null && prev is IDisposable prevDisposable)
                    prevDisposable.Dispose();
                VarMap[name] = value;
            }
        }

        private _Data_(TTarget obj, WeakReference weak) {
            Weak = weak;
            OnInitialize?.Invoke(this, obj);
        }

        public T Get<T>(string name)
            => (T) this[name];

        public void Set<T>(string name, T value)
            => this[name] = value;

        protected void Dispose(bool disposing) {
            foreach (string name in Disposable)
                if (VarMap.TryGetValue(name, out object value) && value is IDisposable valueDisposable)
                    valueDisposable.Dispose();
            VarMap.Clear();
        }

        ~_Data_() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public static _Data_<TTarget> For(TTarget obj) {
            WeakReference weak = new WeakReference(obj);
            if (ObjectMap.TryGetValue(weak, out _Data_<TTarget> data))
                return data;

            data = new _Data_<TTarget>(obj, weak);
            ObjectMap.Add(weak, data);
            return data;
        }

        private sealed class WeakReferenceComparer : EqualityComparer<WeakReference> {
            public override bool Equals(WeakReference x, WeakReference y)
                => ReferenceEquals(x.Target, y.Target) && x.IsAlive == y.IsAlive;

            public override int GetHashCode(WeakReference obj)
                => obj.Target?.GetHashCode() ?? 0;
        }
    }

    public static class _ExtraHelper_ {
        public static event Action Collected;
        static _ExtraHelper_() {
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
