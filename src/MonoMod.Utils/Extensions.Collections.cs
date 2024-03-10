using Mono.Collections.Generic;
using System.Collections;
using System.Collections.Generic;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        /// <summary>
        /// See <see cref="List{T}.AddRange(IEnumerable{T})"/>
        /// </summary>
        public static void AddRange<T>(this Collection<T> list, IEnumerable<T> other)
        {
            Helpers.ThrowIfArgumentNull(list);
            foreach (var entry in Helpers.ThrowIfNull(other))
                list.Add(entry);
        }
        /// <summary>
        /// See <see cref="List{T}.AddRange(IEnumerable{T})"/>
        /// </summary>
        public static void AddRange(this IDictionary dict, IDictionary other)
        {
            Helpers.ThrowIfArgumentNull(dict);
            foreach (DictionaryEntry entry in Helpers.ThrowIfNull(other))
                dict.Add(entry.Key, entry.Value);
        }
        /// <summary>
        /// See <see cref="List{T}.AddRange(IEnumerable{T})"/>
        /// </summary>
        public static void AddRange<TKey, TValue>(this IDictionary<TKey, TValue> dict, IDictionary<TKey, TValue> other)
        {
            Helpers.ThrowIfArgumentNull(dict);
            foreach (var entry in Helpers.ThrowIfNull(other))
                dict.Add(entry.Key, entry.Value);
        }
        /// <summary>
        /// See <see cref="List{T}.AddRange(IEnumerable{T})"/>
        /// </summary>
        public static void AddRange<TKey, TValue>(this Dictionary<TKey, TValue> dict, Dictionary<TKey, TValue> other) where TKey : notnull
        {
            Helpers.ThrowIfArgumentNull(dict);
            foreach (var entry in Helpers.ThrowIfNull(other))
                dict.Add(entry.Key, entry.Value);
        }

        /// <summary>
        /// See <see cref="List{T}.InsertRange(int, IEnumerable{T})"/>
        /// </summary>
        public static void InsertRange<T>(this Collection<T> list, int index, IEnumerable<T> other)
        {
            Helpers.ThrowIfArgumentNull(list);
            foreach (var entry in Helpers.ThrowIfNull(other))
                list.Insert(index++, entry);
        }

    }
}
