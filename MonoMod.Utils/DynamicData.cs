#if !NETFRAMEWORK3
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;

namespace MonoMod.Utils {
    public sealed class DynamicData : DynamicObject {

        private static int CreationsInProgress = 0;

        private static readonly object[] _NoArgs = new object[0];

        public static event Action<DynamicData, Type, object> OnInitialize;

        private static readonly Dictionary<Type, _Cache_> _CacheMap = new Dictionary<Type, _Cache_>();
        private static readonly Dictionary<Type, _Data_> _DataStaticMap = new Dictionary<Type, _Data_>();
        private static readonly Dictionary<WeakReference, _Data_> _DataMap = new Dictionary<WeakReference, _Data_>(new WeakReferenceComparer());

        private readonly WeakReference Weak;
        private object KeepAlive;
        private readonly _Cache_ _Cache;
        private readonly _Data_ _Data;

        private class _Cache_ {
            public readonly Dictionary<string, Func<object, object>> Getters = new Dictionary<string, Func<object, object>>();
            public readonly Dictionary<string, Action<object, object>> Setters = new Dictionary<string, Action<object, object>>();
            public readonly Dictionary<string, Func<object, object[], object>> Methods = new Dictionary<string, Func<object, object[], object>>();

            public _Cache_(Type targetType) {
                if (targetType == null)
                    return;

                foreach (FieldInfo field in targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                    string name = field.Name;
                    Getters[name] = (obj) => field.GetValue(obj);
                    Setters[name] = (obj, value) => field.SetValue(obj, value);
                }

                foreach (PropertyInfo prop in targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                    string name = prop.Name;

                    MethodInfo get = prop.GetGetMethod(true);
                    if (get != null) {
                        Getters[name] = (obj) => get.Invoke(obj, _NoArgs);
                    }

                    MethodInfo set = prop.GetSetMethod(true);
                    if (set != null) {
                        Setters[name] = (obj, value) => set.Invoke(obj, new object[] { value });
                    }
                }

                Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
                foreach (MethodInfo method in targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                    string name = method.Name;
                    if (methods.ContainsKey(name)) {
                        methods[name] = null;
                    } else {
                        methods[name] = method;
                    }
                }

                foreach (KeyValuePair<string, MethodInfo> kvp in methods) {
                    if (kvp.Value == null)
                        continue;

                    FastReflectionDelegate cb = kvp.Value.GetFastDelegate();
                    Methods[kvp.Key] = (target, args) => cb(target, args);
                }
            }
        }


        private class _Data_ {
            public readonly Dictionary<string, Func<object, object>> Getters = new Dictionary<string, Func<object, object>>();
            public readonly Dictionary<string, Action<object, object>> Setters = new Dictionary<string, Action<object, object>>();
            public readonly Dictionary<string, Func<object, object[], object>> Methods = new Dictionary<string, Func<object, object[], object>>();
            public readonly Dictionary<string, object> Data = new Dictionary<string, object>();

            public _Data_(Type type) {
                if (type == null)
                    return;
            }
        }

        public Dictionary<string, Func<object, object>> Getters => _Data.Getters;
        public Dictionary<string, Action<object, object>> Setters => _Data.Setters;
        public Dictionary<string, Func<object, object[], object>> Methods => _Data.Methods;
        public Dictionary<string, object> Data => _Data.Data;

        static DynamicData() {
            _DataHelper_.Collected += () => {
                if (CreationsInProgress != 0)
                    return;

                lock (_DataMap) {
                    HashSet<WeakReference> dead = new HashSet<WeakReference>();

                    foreach (KeyValuePair<WeakReference, _Data_> kvp in _DataMap) {
                        if (kvp.Key.SafeGetIsAlive())
                            continue;
                        dead.Add(kvp.Key);
                    }

                    foreach (WeakReference weak in dead) {
                        _DataMap.Remove(weak);
                    }
                }
            };
        }

        public bool IsAlive => Weak.SafeGetIsAlive();
        public object Target => Weak.SafeGetTarget();
        public Type TargetType { get; private set; }

        public DynamicData(Type type)
            : this(type, null, false) {
        }

        public DynamicData(object obj)
            : this(obj.GetType(), obj, true) {
        }

        public DynamicData(Type type, object obj)
            : this(type, obj, true) {
        }

        public DynamicData(Type type, object obj, bool keepAlive) {
            TargetType = type;

            lock (_CacheMap) {
                if (!_CacheMap.TryGetValue(type, out _Cache)) {
                    _Cache = new _Cache_(type);
                    _CacheMap.Add(type, _Cache);
                }
            }

            if (obj != null) {
                WeakReference weak = new WeakReference(obj);
                
                // Ideally this would be a "no GC region", but that's too new.
                CreationsInProgress++;
                lock (_DataMap) {
                    if (!_DataMap.TryGetValue(weak, out _Data)) {
                        _Data = new _Data_(type);
                        _DataMap.Add(weak, _Data);
                    }
                }
                CreationsInProgress--;
                
                Weak = weak;
                if (keepAlive)
                    KeepAlive = obj;

            } else {
                lock (_DataStaticMap) {
                    if (!_DataStaticMap.TryGetValue(type, out _Data)) {
                        _Data = new _Data_(type);
                        _DataStaticMap.Add(type, _Data);
                    }
                }
            }

            OnInitialize?.Invoke(this, type, obj);
        }

        public void RegisterProperty(string name, Func<object, object> getter, Action<object, object> setter) {
            Getters[name] = getter;
            Setters[name] = setter;
        }

        public void UnregisterProperty(string name) {
            Getters.Remove(name);
            Setters.Remove(name);
        }

        public void RegisterMethod(string name, Func<object, object[], object> cb) {
            Methods[name] = cb;
        }

        public void UnregisterMethod(string name) {
            Methods.Remove(name);
        }

        public object Get(string name) {
            object target = Target;

            if (_Data.Getters.TryGetValue(name, out Func<object, object> cb))
                return cb(target);

            if (_Cache.Getters.TryGetValue(name, out cb))
                return cb(target);

            if (_Data.Data.TryGetValue(name, out object value))
                return value;

            return null;
        }

        public bool TryGet(string name, out object value) {
            object target = Target;

            if (_Data.Getters.TryGetValue(name, out Func<object, object> cb)) {
                value = cb(target);
                return true;
            }

            if (_Cache.Getters.TryGetValue(name, out cb)) {
                value = cb(target);
                return true;
            }

            if (_Data.Data.TryGetValue(name, out value)) {
                return true;
            }

            return false;
        }

        public T Get<T>(string name) {
            return (T) Get(name);
        }

        public bool TryGet<T>(string name, out T value) {
            bool rv = TryGet(name, out object _value);
            value = (T) _value;
            return rv;
        }

        public void Set(string name, object value) {
            object target = Target;

            if (_Data.Setters.TryGetValue(name, out Action<object, object> cb)) {
                cb(target, value);
                return;
            }

            if (_Cache.Setters.TryGetValue(name, out cb)) {
                cb(target, value);
                return;
            }

            Data[name] = value;
        }

        private void Dispose(bool disposing) {
            KeepAlive = default;
        }

        ~DynamicData() {
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


        public override IEnumerable<string> GetDynamicMemberNames() {
            return _Data.Data.Keys
                .Union(_Data.Getters.Keys)
                .Union(_Data.Setters.Keys)
                .Union(_Data.Methods.Keys)
                .Union(_Cache.Getters.Keys)
                .Union(_Cache.Setters.Keys)
                .Union(_Cache.Methods.Keys);
        }

        public override bool TryConvert(ConvertBinder binder, out object result) {
            if (TargetType.IsCompatible(binder.Type) ||
                TargetType.IsCompatible(binder.ReturnType)) {
                result = Target;
                return true;
            }

            if (typeof(DynamicData).IsCompatible(binder.Type) ||
                typeof(DynamicData).IsCompatible(binder.ReturnType)) {
                result = this;
                return true;
            }

            result = null;
            return false;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result) {
            if (Methods.ContainsKey(binder.Name)) {
                result = null;
                return false;
            }

            result = Get(binder.Name);
            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value) {
            Set(binder.Name, value);
            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result) {
            if (_Data.Methods.TryGetValue(binder.Name, out Func<object, object[], object> cb)) {
                result = cb(Target, args);
                return true;
            }

            if (_Cache.Methods.TryGetValue(binder.Name, out cb)) {
                result = cb(Target, args);
                return true;
            }

            result = null;
            return false;
        }

    }
}
#endif
