using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections;

namespace MonoMod.Helpers {
    public class LongDictionary<V> : IDictionary<long, V>, IDictionary {

        static readonly uint[] _PrimeSizes = new uint[] {
            89, 179, 359, 719, 1439, 2879, 5779, 11579, 23159, 46327,
            92657, 185323, 370661, 741337, 1482707, 2965421, 5930887, 11861791,
            23723599, 47447201, 94894427, 189788857, 379577741, 759155483
        };

        private int[] _HashMap;

        private long[] _Keys;
        private V[] _Values;
        private int[] _Next;

        private int _Count;

        public int Count {
            get { return _Count; }
        }

        public bool IsReadOnly {
            get { return false; }
        }

        public V this[long key] {
            get {
                return Get(key);
            }
            set {
                Add(key, value);
            }
        }

        public object this[object key] {
            get {
                return Get((long) key);
            }
            set {
                Add((long) key, (V) value);
            }
        }

        public bool IsFixedSize {
            get { return false; }
        }

        ICollection IDictionary.Keys {
            get { return _Keys.Clone(_Count); }
        }

        public ICollection<long> Keys {
            get { return _Keys.Clone(_Count); }
        }

        ICollection IDictionary.Values {
            get { return _Values.Clone(_Count); }
        }

        public ICollection<V> Values {
            get { return _Values.Clone(_Count); }
        }

        public bool IsSynchronized {
            get { return false; }
        }

        public LongDictionary() {
            Clear();
        }

        private uint FindNewSize() {
            uint min = (uint) _HashMap.Length * 2 + 1;
            for (int i = 0; i < _PrimeSizes.Length; i++)
                if (_PrimeSizes[i] >= min)
                    return _PrimeSizes[i];
            throw new NotImplementedException($"PrimeSizes doesn't contain prime larger than {min}");
        }

        private void Resize(uint size = 0, bool copy = true) {
            if (size == 0)
                size = FindNewSize();

            int[] hashMap = new int[size];
            long[] keys = new long[size];
            V[] values = new V[size];
            int[] next = new int[size];

            for (uint i = size - 1; i > 0; --i) {
                hashMap[i] = -1;
                next[i] = -1;
            }
            hashMap[0] = -1;
            next[0] = -1;

            if (copy) {
                Array.Copy(_Keys, keys, _Count);
                Array.Copy(_Values, values, _Count);

                for (int i = _Count - 1; i > -1; --i) {
                    uint pos = ((uint) keys[i]) % size;
                    int posPrev = hashMap[pos];
                    hashMap[pos] = i;
                    if (posPrev != -1)
                        next[i] = posPrev;
                }
            }

            _HashMap = hashMap;
            _Keys = keys;
            _Values = values;
            _Next = next;
        }

        private int GetPosition(long key) {
            uint hash = (uint) key;
            int pos = _HashMap[hash % (uint) _HashMap.Length];
            if (pos == -1)
                return -1;
            int posNext = pos;
            do {
                if (key == _Keys[posNext])
                    return posNext;
                posNext = _Next[posNext];
            } while (posNext != -1);
            return -1;
        }

        public void Add(long key, V value) {
            if (_Count >= _HashMap.Length)
                Resize();

            uint hash = (uint) key;
            uint hashPos = hash % (uint) _HashMap.Length;
            int entryPos = _HashMap[hashPos];

            int nextPos = entryPos;
            if (entryPos != -1)
                do {
                    if (key == _Keys[nextPos])
                        return;
                    nextPos = _Next[nextPos];
                } while (nextPos != -1);
            nextPos = _Count;

            _HashMap[hashPos] = nextPos;
            _Keys[nextPos] = key;
            _Values[nextPos] = value;
            _Next[nextPos] = entryPos;
            ++_Count;
        }

        public V Get(long key) {
            int pos = GetPosition(key);
            if (pos == -1)
                throw new KeyNotFoundException();
            return _Values[pos];
        }

        public bool ContainsKey(long key)
            => GetPosition(key) != -1;

        public bool TryGetValue(long key, out V value) {
            int pos = GetPosition(key);
            if (pos == -1) {
                value = default(V);
                return false;
            }
            value = _Values[pos];
            return true;
        }

        public void Add(KeyValuePair<long, V> item)
            => Add(item.Key, item.Value);

        void IDictionary<long, V>.Add(long key, V value)
            => Add(key, value);

        public void Add(object key, object value)
            => Add((long) key, (V) value);

        public void Clear()
            => Resize(_PrimeSizes[0], false);

        public bool Contains(KeyValuePair<long, V> item) {
            V value;
            if (!TryGetValue(item.Key, out value))
                return false;
            if (!item.Value.Equals(value))
                return false;
            return true;
        }

        public bool Contains(object key) {
            return Contains((int) key);
        }

        public IEnumerator<KeyValuePair<long, V>> GetEnumerator() {
            for (int i = 0; i < _Count; i++)
                yield return new KeyValuePair<long, V>(_Keys[i], _Values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        IDictionaryEnumerator IDictionary.GetEnumerator()
            => new DictionaryEnumerator(GetEnumerator());

        // Implement those as needed.

        public bool Remove(KeyValuePair<long, V> item) {
            throw new NotImplementedException();
        }

        public void Remove(object key) {
            throw new NotImplementedException();
        }

        public bool Remove(long key) {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<long, V>[] array, int arrayIndex) {
            throw new NotImplementedException();
        }

        public void CopyTo(Array array, int index) {
            throw new NotImplementedException();
        }

        public object SyncRoot {
            get { throw new NotImplementedException(); }
        }



        struct DictionaryEnumerator : IDictionaryEnumerator {
            private IEnumerator<KeyValuePair<long, V>> Enumerator;

            public object Current {
                get {
                    return Entry;
                }
            }

            public DictionaryEntry Entry {
                get {
                    KeyValuePair<long, V> entry = Enumerator.Current;
                    return new DictionaryEntry(entry.Key, entry.Value);
                }
            }

            public object Key {
                get {
                    return Enumerator.Current.Key;
                }
            }

            public object Value {
                get {
                    return Enumerator.Current.Value;
                }
            }

            public DictionaryEnumerator(IEnumerator<KeyValuePair<long, V>> enumerator) {
                Enumerator = enumerator;
            }

            public bool MoveNext()
                => Enumerator.MoveNext();

            public void Reset()
                => Enumerator.Reset();
        }

    }
}
