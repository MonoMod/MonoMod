using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils {
    public sealed class DynData<TTarget> : IDisposable where TTarget : class {

        private static int CreationsInProgress = 0;

        private static readonly object[] _NoArgs = new object[0];

        public static event Action<DynData<TTarget>, TTarget> OnInitialize;

        private static readonly _Data_ _DataStatic = new _Data_();
        private static readonly Dictionary<WeakReference, _Data_> _DataMap = new Dictionary<WeakReference, _Data_>(new WeakReferenceComparer());
        private static readonly Dictionary<string, Func<TTarget, object>> _SpecialGetters = new Dictionary<string, Func<TTarget, object>>();
        private static readonly Dictionary<string, Action<TTarget, object>> _SpecialSetters = new Dictionary<string, Action<TTarget, object>>();

        private readonly WeakReference Weak;
        private TTarget KeepAlive;
        private readonly _Data_ _Data;

        private class _Data_ {
            public readonly Dictionary<string, Func<TTarget, object>> Getters = new Dictionary<string, Func<TTarget, object>>();
            public readonly Dictionary<string, Action<TTarget, object>> Setters = new Dictionary<string, Action<TTarget, object>>();
            public readonly Dictionary<string, object> Data = new Dictionary<string, object>();
            public readonly HashSet<string> Disposable = new HashSet<string>();

            public void Free() {
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
            _DataHelper_.Collected += () => {
                if (CreationsInProgress != 0)
                    return;

                lock (_DataMap) {
                    HashSet<WeakReference> dead = new HashSet<WeakReference>();

                    foreach (KeyValuePair<WeakReference, _Data_> kvp in _DataMap) {
                        if (kvp.Key.SafeGetIsAlive())
                            continue;
                        dead.Add(kvp.Key);
                        kvp.Value.Free();
                    }

                    foreach (WeakReference weak in dead) {
                        _DataMap.Remove(weak);
                    }
                }
            };
            
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

        public bool IsAlive => Weak.SafeGetIsAlive();
        public TTarget Target => Weak.SafeGetTarget() as TTarget;

        public object this[string name] {
            get {
                if (_SpecialGetters.TryGetValue(name, out Func<TTarget, object> cb) ||
                    Getters.TryGetValue(name, out cb))
                    return cb(Weak.SafeGetTarget() as TTarget);

                if (Data.TryGetValue(name, out object value))
                    return value;

                return null;
            }
            set {
                if (_SpecialSetters.TryGetValue(name, out Action<TTarget, object> cb) ||
                    Setters.TryGetValue(name, out cb)) {
                    cb(Weak.SafeGetTarget() as TTarget, value);
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
                
                // Ideally this would be a "no GC region", but that's too new.
                CreationsInProgress++;
                lock (_DataMap) {
                    if (!_DataMap.TryGetValue(weak, out _Data)) {
                        _Data = new _Data_();
                        _DataMap.Add(weak, _Data);
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

        private sealed class WeakReferenceComparer : EqualityComparer<WeakReference> {
            public override bool Equals(WeakReference x, WeakReference y)
                => ReferenceEquals(x.SafeGetTarget(), y.SafeGetTarget()) && x.SafeGetIsAlive() == y.SafeGetIsAlive();

            public override int GetHashCode(WeakReference obj)
                => obj.SafeGetTarget()?.GetHashCode() ?? 0;
        }
    }

    internal static class _DataHelper_ {
        public static event Action Collected;
        private static bool Unloading;

        static _DataHelper_() {
            new CollectionDummy();

#if NETSTANDARD
            Type t_AssemblyLoadContext = typeof(Assembly).GetTypeInfo().Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
            if (t_AssemblyLoadContext != null) {
                object alc = t_AssemblyLoadContext.GetMethod("GetLoadContext").Invoke(null, new object[] { typeof(_DataHelper_).Assembly });
                EventInfo e_Unloading = t_AssemblyLoadContext.GetEvent("Unloading");
                e_Unloading.AddEventHandler(alc, Delegate.CreateDelegate(
                    e_Unloading.EventHandlerType,
                    typeof(_DataHelper_).GetMethod("UnloadingALC", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(t_AssemblyLoadContext)
                ));
            }
#endif
        }

#if NETSTANDARD
#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE0060 // Remove unused parameter
        private static void UnloadingALC<T>(T alc) {
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning restore IDE0060 // Remove unused parameter
            Unloading = true;
        }
#endif

        private sealed class CollectionDummy {
            ~CollectionDummy() {
                Unloading |= AppDomain.CurrentDomain.IsFinalizingForUnload();

                if (!Unloading)
                    GC.ReRegisterForFinalize(this);

                Collected?.Invoke();
            }
        }

        internal static object SafeGetTarget(this WeakReference weak) {
            try {
                return weak.Target;
            } catch (InvalidOperationException) {
                // FUCK OLD UNITY MONO
                // https://github.com/Unity-Technologies/mono/blob/unity-2017.4/mcs/class/corlib/System/WeakReference.cs#L96
                // https://github.com/Unity-Technologies/mono/blob/unity-2017.4-mbe/mcs/class/corlib/System/WeakReference.cs#L94
                // https://docs.microsoft.com/en-us/archive/blogs/yunjin/trivial-debugging-note-using-weakreference-in-finalizer
                // "So on CLR V2.0 offical released build, you could safely use WeakReference in finalizer."
                return null;
            }
        }

        internal static bool SafeGetIsAlive(this WeakReference weak) {
            try {
                return weak.IsAlive;
            } catch (InvalidOperationException) {
                // See above FUCK OLD UNITY MONO note.
                return false;
            }
        }
    }
}
