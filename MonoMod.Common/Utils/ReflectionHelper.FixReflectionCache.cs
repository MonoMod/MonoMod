using System;
using System.Reflection;
using SRE = System.Reflection.Emit;
using CIL = Mono.Cecil.Cil;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Mono.Cecil;
using System.Text;
using Mono.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections;

namespace MonoMod.Utils {
#if !MONOMOD_INTERNAL
    public
#endif
    static partial class ReflectionHelper {

        // .NET Framework can break member ordering if using Module.Resolve* on certain members.

        private static readonly object[] _CacheGetterArgs = { /* MemberListType.All */ 0, /* name apparently always null? */ null };

        private static Type t_RuntimeType =
            typeof(Type).Assembly
            .GetType("System.RuntimeType");

        private static PropertyInfo p_RuntimeType_Cache =
            typeof(Type).Assembly
            .GetType("System.RuntimeType")
            ?.GetProperty("Cache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo m_RuntimeTypeCache_GetFieldList =
            typeof(Type).Assembly
            .GetType("System.RuntimeType+RuntimeTypeCache")
            ?.GetMethod("GetFieldList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo m_RuntimeTypeCache_GetPropertyList =
            typeof(Type).Assembly
            .GetType("System.RuntimeType+RuntimeTypeCache")
            ?.GetMethod("GetPropertyList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

#if !NETFRAMEWORK3
        private static readonly ConditionalWeakTable<Type, CacheFixEntry> _CacheFixed = new ConditionalWeakTable<Type, CacheFixEntry>();
#else
        private static readonly Dictionary<WeakReference, CacheFixEntry> _CacheFixed = new Dictionary<WeakReference, CacheFixEntry>(new WeakReferenceComparer());
        private static readonly HashSet<WeakReference> _CacheFixedDead = new HashSet<WeakReference>();
#endif

        static ReflectionHelper() {
#if NETFRAMEWORK3
            GCListener.OnCollect += () => {
                lock (_CacheFixed) {
                    foreach (KeyValuePair<WeakReference, CacheFixEntry> kvp in _CacheFixed) {
                        if (kvp.Key.SafeGetIsAlive())
                            continue;
                        _CacheFixedDead.Add(kvp.Key);
                    }

                    foreach (WeakReference weak in _CacheFixedDead) {
                        _CacheFixed.Remove(weak);
                    }

                    _CacheFixedDead.Clear();
                }
            };
#endif
        }

        public static void FixReflectionCacheAuto(this Type type) {
#if NETFRAMEWORK
            FixReflectionCache(type);
#endif
        }

        public static void FixReflectionCache(this Type type) {
            if (t_RuntimeType == null ||
                p_RuntimeType_Cache == null ||
                m_RuntimeTypeCache_GetFieldList == null ||
                m_RuntimeTypeCache_GetPropertyList == null)
                return;

            for (; type != null; type = type.DeclaringType) {
                // All types SHOULD inherit RuntimeType, including those built at runtime.
                // One might never know what awaits us in the depths of reflection hell though.
                if (!t_RuntimeType.IsInstanceOfType(type))
                    continue;

#if NETFRAMEWORK3
                WeakReference key = new WeakReference(type);
                CacheFixEntry entry;
                lock (_CacheFixed) {
                    if (!_CacheFixed.TryGetValue(key, out entry)) {
                        _CacheFixed.Add(key, entry = new CacheFixEntry());
                        // All RuntimeTypes MUST have a cache, the getter is non-virtual, it creates on demand and asserts non-null.
                        object cache;
                        entry.Cache = cache = p_RuntimeType_Cache.GetValue(type, _NoArgs);
                        entry.Properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList);
                        entry.Fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList);
                    } else if (!_Verify(entry, type)) {
                        continue;
                    }
                }

                lock (entry) {
                    _FixReflectionCacheOrder<PropertyInfo>(entry.Properties);
                    _FixReflectionCacheOrder<FieldInfo>(entry.Fields);
                }

#else
                CacheFixEntry entry = _CacheFixed.GetValue(type, rt => {
                    CacheFixEntry entryNew = new CacheFixEntry();
                    object cache;
                    Array properties, fields;

                    // All RuntimeTypes MUST have a cache, the getter is non-virtual, it creates on demand and asserts non-null.
                    entryNew.Cache = cache = p_RuntimeType_Cache.GetValue(rt, _NoArgs);
                    entryNew.Properties = properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList);
                    entryNew.Fields = fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList);

                    _FixReflectionCacheOrder<PropertyInfo>(properties);
                    _FixReflectionCacheOrder<FieldInfo>(fields);

                    entryNew.NeedsVerify = false;
                    return entryNew;
                });

                if (entry.NeedsVerify && !_Verify(entry, type)) {
                    lock (entry) {
                        _FixReflectionCacheOrder<PropertyInfo>(entry.Properties);
                        _FixReflectionCacheOrder<FieldInfo>(entry.Fields);
                    }
                }

                entry.NeedsVerify = true;
#endif
            }
        }

        private static bool _Verify(CacheFixEntry entry, Type type) {
            object cache;
            Array properties, fields;

            // The cache can sometimes be invalidated.
            // TODO: Figure out if only the arrays get replaced or if the entire cache object gets replaced!
            if (entry.Cache != (cache = p_RuntimeType_Cache.GetValue(type, _NoArgs))) {
                entry.Cache = cache;
                entry.Properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList);
                entry.Fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList);
                return false;

            } else if (entry.Properties != (properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList))) {
                entry.Properties = properties;
                entry.Fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList);
                return false;

            } else if (entry.Fields != (fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList))) {
                entry.Fields = fields;
                return false;

            } else {
                // Cache should still be the same, no re-fix necessary.
                return true;
            }
        }

        private static Array _GetArray(object cache, MethodInfo getter) {
            // Get and discard once, otherwise we might not be getting the actual backing array.
            getter.Invoke(cache, _CacheGetterArgs);
            return (Array) getter.Invoke(cache, _CacheGetterArgs);
        }

        private static void _FixReflectionCacheOrder<T>(Array orig) where T : MemberInfo {
            // Sort using a short-lived list.
            List<T> list = new List<T>(orig.Length);
            for (int i = 0; i < orig.Length; i++)
                list.Add((T) orig.GetValue(i));

            list.Sort((a, b) => a.MetadataToken - b.MetadataToken);

            for (int i = orig.Length - 1; i >= 0; --i)
                orig.SetValue(list[i], i);
        }

        private class CacheFixEntry {
            public object Cache;
            public Array Properties;
            public Array Fields;
#if !NETFRAMEWORK3
            public bool NeedsVerify;
#endif
        }

    }
}
