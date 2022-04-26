#if NET40_OR_GREATER && !NETSTANDARD2_1_OR_GREATER  && !NETCOREAPP2_0_OR_GREATER
#define CWT_NOT_ENUMERABLE
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;

namespace System.Runtime.CompilerServices {

    // WE use this for our implementation of CWT so that Roslyn always uses this extension method to enumerate it on old frameworks
    internal interface ICWTEnumerable<T> {
        IEnumerator<T> GetEnumerator();
    }

    public static class ConditionalWeakTableExtensions {
#if CWT_NOT_ENUMERABLE
        private static class CWTInfoHolder<TKey, TValue> where TKey : class where TValue : class? {
            private static readonly MethodInfo? get_KeysMethod;
            public static readonly GetKeys? get_Keys;

            public delegate IEnumerable<TKey> GetKeys(ConditionalWeakTable<TKey, TValue> cwt);

            static CWTInfoHolder() {
                get_KeysMethod = typeof(ConditionalWeakTable<TKey, TValue>).GetProperty("Keys", BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(nonPublic: true);
                if (get_KeysMethod is not null) {
                    get_Keys = (GetKeys) Delegate.CreateDelegate(typeof(GetKeys), get_KeysMethod);
                }
            }
        }
#endif

        [SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code",
            Justification = "This check is expected to be always true for some targets.")]
        public static IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> self) where TKey : class where TValue : class? {
            if (self is null) {
                ThrowHelper.ThrowArgumentNullException(nameof(self));
                // dum dum compiler
                return null!;
            } else if (self is ICWTEnumerable<KeyValuePair<TKey, TValue>> cwtEnum) {
                return cwtEnum.GetEnumerator();
            } else if (self is IEnumerable<KeyValuePair<TKey, TValue>> enumerable) {
                return enumerable.GetEnumerator();
            } else {
#if !CWT_NOT_ENUMERABLE
                throw new PlatformNotSupportedException("This version of MonoMod.Backports was built targeting a version of the framework " +
                    "where ConditionalWeakTable is enumerable, but it isn't!");
#else
                if (CWTInfoHolder<TKey, TValue>.get_Keys is { } getKeys) {
                    return Enumerate(self, getKeys(self));
                    static IEnumerator<KeyValuePair<TKey, TValue>> Enumerate(ConditionalWeakTable<TKey, TValue> cwt, IEnumerable<TKey> keys) {
                        foreach (var key in keys) {
                            if (cwt.TryGetValue(key, out TValue? value)) {
                                yield return new KeyValuePair<TKey, TValue>(key, value);
                            }
                        }
                    }
                } else {
                    throw new PlatformNotSupportedException("Could not get Keys property of ConditionalWeakTable to enumerate it");
                }
#endif
            }
        }
    }
}
