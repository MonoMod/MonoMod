#if NETFRAMEWORK && !NET40_OR_GREATER
#define BACKPORTS_IMPL
#endif

#if BACKPORTS_IMPL || NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
#define HAS_BAG_CLEAR
#define HAS_QUEUE_CLEAR
#endif
#if BACKPORTS_IMPL || NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER || NET472_OR_GREATER
#define HAS_ADD_OR_UPDATE_ARG
#define HAS_GET_OR_ADD_ARG
#endif
#if BACKPORTS_IMPL || NET5_0_OR_GREATER
#define HAS_TRYREMOVE_KVP
#endif

using System.Collections.Generic;

namespace System.Collections.Concurrent
{
    public static class ConcurrentExtensions
    {
        public static void Clear<T>(this ConcurrentBag<T> bag)
        {
            ThrowHelper.ThrowIfArgumentNull(bag, nameof(bag));
#if HAS_BAG_CLEAR
            bag.Clear();
#else
            // If we don't actually have .Clear(), this is unfortunately the best we can do
            while (bag.TryTake(out _))
                ;
#endif
        }
        public static void Clear<T>(this ConcurrentQueue<T> queue)
        {
            ThrowHelper.ThrowIfArgumentNull(queue, nameof(queue));
#if HAS_QUEUE_CLEAR
            queue.Clear();
#else
            // If we don't actually have .Clear(), this is unfortunately the best we can do
            while (queue.TryDequeue(out _))
                ;
#endif
        }

        public static TValue AddOrUpdate<TKey, TValue, TArg>(this ConcurrentDictionary<TKey, TValue> dict,
            TKey key, Func<TKey, TArg, TValue> addValueFactory, Func<TKey, TValue, TArg, TValue> updateValueFactory, TArg factoryArgument)
            where TKey : notnull
        {
            ThrowHelper.ThrowIfArgumentNull(dict, nameof(dict));
#if HAS_ADD_OR_UPDATE_ARG
            return dict.AddOrUpdate(key, addValueFactory, updateValueFactory, factoryArgument);
#else
            // if we don't have that method, we can just closure it with another overload
            return dict.AddOrUpdate(key, k => addValueFactory(k, factoryArgument), (k, v) => updateValueFactory(k, v, factoryArgument));
#endif
        }

        public static TValue GetOrAdd<TKey, TValue, TArg>(this ConcurrentDictionary<TKey, TValue> dict,
            TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
            where TKey : notnull
        {
            ThrowHelper.ThrowIfArgumentNull(dict, nameof(dict));
#if HAS_GET_OR_ADD_ARG
            return dict.GetOrAdd(key, valueFactory, factoryArgument);
#else
            // if we don't have that method, we can just closure it with another overload
            return dict.GetOrAdd(key, k => valueFactory(k, factoryArgument));
#endif
        }

        public static bool TryRemove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dict, KeyValuePair<TKey, TValue> item)
            where TKey : notnull
        {
            ThrowHelper.ThrowIfArgumentNull(dict, nameof(dict));
#if HAS_TRYREMOVE_KVP
            return dict.TryRemove(item);
#else
            if (dict.TryRemove(item.Key, out var value)) {
                if (EqualityComparer<TValue>.Default.Equals(item.Value, value)) {
                    return true;
                } else {
                    _ = dict.AddOrUpdate(item.Key, _ => value, (_, _) => value);
                    return false;
                }
            }
            return false;
#endif
        }

        // I *would* provide an extension GetComparer(), except I don't think its reasonably possible before .NET 6, when .Comparer
        // is added. I figure this is not exactly a critically useful API anyway, so its fine to have it just not be present.

    }
}
