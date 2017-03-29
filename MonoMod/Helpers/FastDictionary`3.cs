using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections;
using MonoMod.NET40Shim;

namespace MonoMod.Helpers {
    public class FastDictionary<K1, K2, V> {

        static readonly uint[] _PrimeSizes = new uint[] {
            89, 179, 359, 719, 1439, 2879, 5779, 11579, 23159, 46327,
            92657, 185323, 370661, 741337, 1482707, 2965421, 5930887, 11861791,
            23723599, 47447201, 94894427, 189788857, 379577741, 759155483
        };

        private int[] _HashMap;

        private K1[] _Keys1;
        private K2[] _Keys2;
        private V[] _Values;
        private int[] _Next;

        private int _Count;

        public int Count {
            get { return _Count; }
        }

        public bool IsReadOnly {
            get { return false; }
        }

        public V this[K1 key1, K2 key2] {
            get {
                return Get(key1, key2);
            }
            set {
                Add(key1, key2, value);
            }
        }

        public FastDictionary() {
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
            K1[] keys1 = new K1[size];
            K2[] keys2 = new K2[size];
            V[] values = new V[size];
            int[] next = new int[size];

            for (uint i = size - 1; i > 0; --i) {
                hashMap[i] = -1;
                next[i] = -1;
            }
            hashMap[0] = -1;
            next[0] = -1;

            if (copy) {
                Array.Copy(_Keys1, keys1, _Count);
                Array.Copy(_Keys2, keys2, _Count);
                Array.Copy(_Values, values, _Count);

                for (int i = _Count - 1; i > -1; --i) {
                    int hash1 = keys1[i].GetHashCode();
                    int hash2 = keys2[i].GetHashCode();
                    uint pos = ((uint) (hash1 + hash2 % hash1 + hash2)) % size;
                    int posPrev = hashMap[pos];
                    hashMap[pos] = i;
                    if (posPrev != -1)
                        next[i] = posPrev;
                }
            }

            _HashMap = hashMap;
            _Keys1 = keys1;
            _Keys2 = keys2;
            _Values = values;
            _Next = next;
        }

        private int GetPosition(K1 key1, K2 key2) {
            int hash1 = key1.GetHashCode();
            int hash2 = key2.GetHashCode();
            uint hash = (uint) (hash1 + hash2 % hash1 + hash2);
            int pos = _HashMap[hash % (uint) _HashMap.Length];
            if (pos == -1)
                return -1;
            int posNext = pos;
            do {
                if (key1.Equals(_Keys1[posNext]) && key2.Equals(_Keys2[posNext]))
                    return posNext;
                posNext = _Next[posNext];
            } while (posNext != -1);
            return -1;
        }

        public void Add(K1 key1, K2 key2, V value) {
            if (_Count >= _HashMap.Length)
                Resize();

            int hash1 = key1.GetHashCode();
            int hash2 = key2.GetHashCode();
            uint hash = (uint) (hash1 + hash2 % hash1 + hash2);
            uint posHash = hash % (uint) _HashMap.Length;
            int pos = _HashMap[posHash];

            int posNext = pos;
            if (pos != -1)
                do {
                    if (key1.Equals(_Keys1[posNext]) && key2.Equals(_Keys2[posNext]))
                        return;
                    posNext = _Next[posNext];
                } while (posNext != -1);
            posNext = _Count;

            _HashMap[posHash] = posNext;
            _Keys1[posNext] = key1;
            _Keys2[posNext] = key2;
            _Values[posNext] = value;
            _Next[posNext] = pos;
            ++_Count;
        }

        public V Get(K1 key1, K2 key2) {
            int pos = GetPosition(key1, key2);
            if (pos == -1)
                throw new KeyNotFoundException();
            return _Values[pos];
        }

        public bool ContainsKey(K1 key1, K2 key2)
            => GetPosition(key1, key2) != -1;

        public bool TryGetValue(K1 key1, K2 key2, out V value) {
            int pos = GetPosition(key1, key2);
            if (pos == -1) {
                value = default(V);
                return false;
            }
            value = _Values[pos];
            return true;
        }

        public void Clear()
            => Resize(_PrimeSizes[0], false);

        // Implement other IDictionary methods as needed.

    }
}
