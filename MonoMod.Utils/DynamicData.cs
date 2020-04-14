#if !NETFRAMEWORK3
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils {
    public sealed class DynamicData : DynamicObject, IEnumerable<KeyValuePair<string, object>> {

        private static readonly object[] _NoArgs = new object[0];

        public static event Action<DynamicData, Type, object> OnInitialize;

        private static readonly Dictionary<Type, _Cache_> _CacheMap = new Dictionary<Type, _Cache_>();
        private static readonly Dictionary<Type, _Data_> _DataStaticMap = new Dictionary<Type, _Data_>();
        private static readonly ConditionalWeakTable<object, _Data_> _DataMap = new ConditionalWeakTable<object, _Data_>();

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
                lock (_DataMap) {
                    if (!_DataMap.TryGetValue(obj, out _Data)) {
                        _Data = new _Data_(type);
                        _DataMap.Add(obj, _Data);
                    }
                }
                
                Weak = new WeakReference(obj);
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

        public static Func<object, T> New<T>(params object[] args) {
            T target = (T) Activator.CreateInstance(typeof(T), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            return other => Set(target, other);
        }

        public static Func<object, object> New(Type type, params object[] args) {
            object target = Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            return other => Set(target, other);
        }

        public static Func<object, dynamic> NewWrap<T>(params object[] args) {
            T target = (T) Activator.CreateInstance(typeof(T), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            return other => Wrap(target, other);
        }

        public static Func<object, dynamic> NewWrap(Type type, params object[] args) {
            object target = Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, args, null);
            return other => Wrap(target, other);
        }

        public static dynamic Wrap(object target, object other = null) {
            DynamicData data = new DynamicData(target);
            data.CopyFrom(other);
            return data;
        }

        public static T Set<T>(T target, object other = null) {
            return (T) Set((object) target, other);
        }

        public static object Set(object target, object other = null) {
            DynamicData data = new DynamicData(target);
            data.CopyFrom(other);
            return data.Target;
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

        public void CopyFrom(object other) {
            if (other == null)
                return;
            foreach (PropertyInfo prop in other.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Set(prop.Name, prop.GetValue(other, null));
        }

        public object Get(string name) {
            TryGet(name, out object value);
            return value;
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

        public void Add(KeyValuePair<string, object> kvp) {
            Set(kvp.Key, kvp.Value);
        }

        public void Add(string key, object value) {
            Set(key, value);
        }

        public object Invoke(string name, params object[] args) {
            TryInvoke(name, args, out object result);
            return result;
        }

        public bool TryInvoke(string name, object[] args, out object result) {
            if (_Data.Methods.TryGetValue(name, out Func<object, object[], object> cb)) {
                result = cb(Target, args);
                return true;
            }

            if (_Cache.Methods.TryGetValue(name, out cb)) {
                result = cb(Target, args);
                return true;
            }

            result = null;
            return false;
        }

        public T Invoke<T>(string name, params object[] args) {
            return (T) Invoke(name, args);
        }

        public bool TryInvoke<T>(string name, object[] args, out T result) {
            bool rv = TryInvoke(name, args, out object _result);
            result = (T) _result;
            return rv;
        }

        private void Dispose(bool disposing) {
            KeepAlive = null;
        }

        ~DynamicData() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
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
                TargetType.IsCompatible(binder.ReturnType) ||
                binder.Type == typeof(object) ||
                binder.ReturnType == typeof(object)) {
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
            return TryInvoke(binder.Name, args, out result);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() {
            foreach (string name
                in _Data.Data.Keys
                .Union(_Data.Getters.Keys)
                .Union(_Data.Setters.Keys)
                .Union(_Cache.Getters.Keys)
                .Union(_Cache.Setters.Keys))
                yield return new KeyValuePair<string, object>(name, Get(name));
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

    }
}
#endif
