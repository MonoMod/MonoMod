using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils
{
    public static partial class ReflectionHelper
    {

        // .NET Framework can break member ordering if using Module.Resolve* on certain members.

        private static readonly object?[] _CacheGetterArgs = { /* MemberListType.All */ 0, /* name apparently always null? */ null };

        private static Type t_RuntimeType =
            typeof(Type).Assembly
            .GetType("System.RuntimeType")
            ?? throw new InvalidOperationException("Could not find RuntimeType");

        private static Type? t_RuntimeTypeCache =
            t_RuntimeType.GetNestedType("RuntimeTypeCache", BindingFlags.Public | BindingFlags.NonPublic);

        private static PropertyInfo? p_RuntimeType_Cache =
            t_RuntimeTypeCache == null ? null :
            t_RuntimeType
            ?.GetProperty("Cache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, t_RuntimeTypeCache, Type.EmptyTypes, null);

        private static MethodInfo? m_RuntimeTypeCache_GetFieldList =
            t_RuntimeTypeCache
            ?.GetMethod("GetFieldList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo? m_RuntimeTypeCache_GetPropertyList =
            t_RuntimeTypeCache
            ?.GetMethod("GetPropertyList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly ConditionalWeakTable<Type, CacheFixEntry> _CacheFixed = new ConditionalWeakTable<Type, CacheFixEntry>();

        static ReflectionHelper()
        {
        }

        public static void FixReflectionCacheAuto(this Type type)
        {
#if NETFRAMEWORK
            FixReflectionCache(type);
#endif
        }

        public static void FixReflectionCache(this Type? type)
        {
            if (t_RuntimeType == null ||
                p_RuntimeType_Cache == null ||
                m_RuntimeTypeCache_GetFieldList == null ||
                m_RuntimeTypeCache_GetPropertyList == null)
                return;

            for (; type != null; type = type.DeclaringType)
            {
                // All types SHOULD inherit RuntimeType, including those built at runtime.
                // One might never know what awaits us in the depths of reflection hell though.
                if (!t_RuntimeType.IsInstanceOfType(type))
                    continue;

                var entry = _CacheFixed.GetValue(type, rt =>
                {
                    var entryNew = new CacheFixEntry();
                    object cache;
                    Array properties, fields;

                    // All RuntimeTypes MUST have a cache, the getter is non-virtual, it creates on demand and asserts non-null.
                    entryNew.Cache = cache = p_RuntimeType_Cache.GetValue(rt, ArrayEx.Empty<object?>())!;
                    entryNew.Properties = properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList);
                    entryNew.Fields = fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList);

                    _FixReflectionCacheOrder<PropertyInfo>(properties);
                    _FixReflectionCacheOrder<FieldInfo>(fields);

                    entryNew.NeedsVerify = false;
                    return entryNew;
                });

                if (entry.NeedsVerify && !_Verify(entry, type))
                {
                    lock (entry)
                    {
                        _FixReflectionCacheOrder<PropertyInfo>(entry.Properties);
                        _FixReflectionCacheOrder<FieldInfo>(entry.Fields);
                    }
                }

                entry.NeedsVerify = true;
            }
        }

        private static bool _Verify(CacheFixEntry entry, Type type)
        {
            object? cache;
            Array properties, fields;

            // The cache can sometimes be invalidated.
            // TODO: Figure out if only the arrays get replaced or if the entire cache object gets replaced!
            if (entry.Cache != (cache = p_RuntimeType_Cache!.GetValue(type, ArrayEx.Empty<object?>())))
            {
                entry.Cache = cache;
                entry.Properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList!);
                entry.Fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList!);
                return false;

            }
            else if (entry.Properties != (properties = _GetArray(cache, m_RuntimeTypeCache_GetPropertyList!)))
            {
                entry.Properties = properties;
                entry.Fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList!);
                return false;

            }
            else if (entry.Fields != (fields = _GetArray(cache, m_RuntimeTypeCache_GetFieldList!)))
            {
                entry.Fields = fields;
                return false;

            }
            else
            {
                // Cache should still be the same, no re-fix necessary.
                return true;
            }
        }

        private static Array _GetArray(object? cache, MethodInfo getter)
        {
            // Get and discard once, otherwise we might not be getting the actual backing array.
            getter.Invoke(cache, _CacheGetterArgs);
            var obj = getter.Invoke(cache, _CacheGetterArgs)!;
            if (obj is Array array)
                return array;
            if (getter.ReturnType is { Namespace: "System.Reflection", Name: "CerArrayList`1" } cerArrayListType)
            {
                var m_array = cerArrayListType.GetField("m_array", _BindingFlagsAll)!;
                // TODO: assert m_array is non-null
                return (Array)m_array.GetValue(obj)!;
            }
            throw new InvalidOperationException($"Unknown reflection cache type {obj.GetType()}");
        }

        private static void _FixReflectionCacheOrder<T>(Array? orig) where T : MemberInfo
        {
            if (orig is null)
                return;
            // Sort using a short-lived list.
            var list = new List<T?>(orig.Length);
            for (var i = 0; i < orig.Length; i++)
                list.Add((T?)orig.GetValue(i)!);

            list.Sort((a, b) =>
            {
                if (a == b)
                    return 0;
                if (a is null)
                    return 1;
                if (b is null)
                    return -1;
                return a.MetadataToken - b.MetadataToken;
            });

            for (var i = orig.Length - 1; i >= 0; --i)
                orig.SetValue(list[i], i);
        }

        private class CacheFixEntry
        {
            public object? Cache;
            public Array? Properties;
            public Array? Fields;
            public bool NeedsVerify;
        }

    }
}
