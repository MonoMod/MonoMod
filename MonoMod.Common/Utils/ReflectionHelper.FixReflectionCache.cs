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
        private static readonly ConditionalWeakTable<Type, object> _CacheFixed = new ConditionalWeakTable<Type, object>();
#else
        private static readonly Dictionary<WeakReference, object> _CacheFixed = new Dictionary<WeakReference, object>(new WeakReferenceComparer());
        private static readonly HashSet<WeakReference> _CacheFixedDead = new HashSet<WeakReference>();
#endif

        static ReflectionHelper() {
#if NETFRAMEWORK3
            GCListener.OnCollect += () => {
                lock (_CacheFixed) {
                    foreach (KeyValuePair<WeakReference, object> kvp in _CacheFixed) {
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
                lock (_CacheFixed) {
                    if (_CacheFixed.ContainsKey(key))
                        continue;
                    _CacheFixed.Add(key, new object());
                }
                Type rt = type;
                {
#else
                _CacheFixed.GetValue(type, rt => {
#endif

                    // All RuntimeTypes MUST have a cache, the getter is non-virtual, it creates on demand and asserts non-null.
                    object cache = p_RuntimeType_Cache.GetValue(rt, _NoArgs);
                    _FixReflectionCacheOrder<PropertyInfo>(cache, m_RuntimeTypeCache_GetPropertyList);
                    _FixReflectionCacheOrder<FieldInfo>(cache, m_RuntimeTypeCache_GetFieldList);

#if !NETFRAMEWORK3
                    return new object();
                });
#else
                }
#endif
            }
        }

        private static void _FixReflectionCacheOrder<T>(object cache, MethodInfo getter) where T : MemberInfo {
            // Get and discard once, otherwise we might not be getting the actual backing array.
            getter.Invoke(cache, _CacheGetterArgs);
            Array orig = (Array) getter.Invoke(cache, _CacheGetterArgs);

            // Sort using a short-lived list.
            List<T> list = new List<T>(orig.Length);
            for (int i = 0; i < orig.Length; i++)
                list.Add((T) orig.GetValue(i));

            list.Sort((a, b) => a.MetadataToken - b.MetadataToken);

            for (int i = orig.Length - 1; i >= 0; --i)
                orig.SetValue(list[i], i);
        }

    }
}
