using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils
{
    public sealed class DynData<TTarget> : IDisposable where TTarget : class
    {

        public static event Action<DynData<TTarget>, TTarget?>? OnInitialize;

        private static readonly _Data_ _DataStatic = new();
        private static readonly ConditionalWeakTable<object, _Data_> _DataMap = new();
        private static readonly Dictionary<string, Func<TTarget, object?>> _SpecialGetters = new();
        private static readonly Dictionary<string, Action<TTarget, object?>> _SpecialSetters = new();

        private readonly WeakReference? Weak;
        [SuppressMessage("CodeQuality", "IDE0052:Remove unread private members",
            Justification = "This is used to keep the target alive.")]
        private TTarget? KeepAlive;
        private readonly _Data_ _Data;

        private class _Data_ : IDisposable
        {
            public readonly Dictionary<string, Func<TTarget, object?>> Getters = new();
            public readonly Dictionary<string, Action<TTarget, object?>> Setters = new();
            public readonly Dictionary<string, object?> Data = new();
            public readonly HashSet<string> Disposable = new();

            ~_Data_()
            {
                Dispose();
            }

            public void Dispose()
            {
                lock (Data)
                {
                    if (Data.Count == 0)
                        return;

                    foreach (var name in Disposable)
                        if (Data.TryGetValue(name, out var value) && value is IDisposable valueDisposable)
                            valueDisposable.Dispose();
                    Disposable.Clear();

                    Data.Clear();
                }
                GC.SuppressFinalize(this);
            }
        }

        public Dictionary<string, Func<TTarget, object?>> Getters => _Data.Getters;
        public Dictionary<string, Action<TTarget, object?>> Setters => _Data.Setters;
        public Dictionary<string, object?> Data => _Data.Data;

        static DynData()
        {

            foreach (var field in typeof(TTarget).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var name = field.Name;
                _SpecialGetters[name] = (obj) => field.GetValue(obj);
                _SpecialSetters[name] = (obj, value) => field.SetValue(obj, value);
            }

            foreach (var prop in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var name = prop.Name;

                var get = prop.GetGetMethod(true);
                if (get != null)
                {
                    _SpecialGetters[name] = (obj) => get.Invoke(obj, ArrayEx.Empty<object?>());
                }

                var set = prop.GetSetMethod(true);
                if (set != null)
                {
                    _SpecialSetters[name] = (obj, value) => set.Invoke(obj, new[] { value });
                }
            }
        }

        public bool IsAlive => Weak == null || Weak.SafeGetIsAlive();
        public TTarget Target => (TTarget)Weak?.SafeGetTarget()!;

        public object? this[string name]
        {
            get
            {
                if (_SpecialGetters.TryGetValue(name, out var cb) ||
                    Getters.TryGetValue(name, out cb))
                    return cb(Target);

                if (Data.TryGetValue(name, out var value))
                    return value;

                return null;
            }
            set
            {
                if (_SpecialSetters.TryGetValue(name, out var cb) ||
                    Setters.TryGetValue(name, out cb))
                {
                    cb(Target, value);
                    return;
                }

                object? prev;
                if (_Data.Disposable.Contains(name) && (prev = this[name]) != null && prev is IDisposable prevDisposable)
                    prevDisposable.Dispose();
                Data[name] = value;
            }
        }

        public DynData()
            : this(null, false)
        {
        }

        public DynData(TTarget? obj)
            : this(obj, true)
        {
        }

        public DynData(TTarget? obj, bool keepAlive)
        {
            if (obj != null)
            {
                var weak = new WeakReference(obj);

                object key = obj;

                if (!_DataMap.TryGetValue(key, out var data))
                {
                    data = new _Data_();
                    _DataMap.Add(key, data);
                }
                _Data = data;

                Weak = weak;
                if (keepAlive)
                    KeepAlive = obj;

            }
            else
            {
                _Data = _DataStatic;
            }

            OnInitialize?.Invoke(this, obj);
        }

        public T? Get<T>(string name)
            => (T?)this[name];

        public void Set<T>(string name, T value)
            => this[name] = value;

        public void RegisterProperty(string name, Func<TTarget, object?> getter, Action<TTarget, object?> setter)
        {
            Getters[name] = getter;
            Setters[name] = setter;
        }

        public void UnregisterProperty(string name)
        {
            Getters.Remove(name);
            Setters.Remove(name);
        }

        private void Dispose(bool disposing)
        {
            KeepAlive = default;
            if (disposing)
            {
                _Data.Dispose();
            }
        }

        ~DynData()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

    }
}
