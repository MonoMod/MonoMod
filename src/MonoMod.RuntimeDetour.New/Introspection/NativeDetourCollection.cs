using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.RuntimeDetour {
    public sealed class NativeDetourCollection : IEnumerable<NativeDetourInfo> {
        private readonly FunctionDetourInfo mdi;
        internal NativeDetourCollection(FunctionDetourInfo mdi)
            => this.mdi = mdi;

        public Enumerator GetEnumerator() => new(mdi);

        IEnumerator<NativeDetourInfo> IEnumerable<NativeDetourInfo>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<NativeDetourInfo> {
            private readonly FunctionDetourInfo mdi;
            private DetourManager.NativeChainNode? curNode;
            private int version;

            internal Enumerator(FunctionDetourInfo mdi) {
                this.mdi = mdi;
                version = mdi.state.detourChainVersion;
                curNode = mdi.state.detourList;
            }

            public NativeDetourInfo Current => mdi.GetDetourInfo(((DetourManager.NativeDetourChainNode) curNode!).Detour);

            object IEnumerator.Current => Current;

            [MemberNotNullWhen(true, nameof(curNode))]
            public bool MoveNext() {
                if (version != mdi.state.detourChainVersion)
                    throw new InvalidOperationException("The detour chain was modified while enumerating");
                curNode = curNode?.Next;
                return curNode is not null;
            }

            public void Reset() {
                version = mdi.state.detourChainVersion;
                curNode = mdi.state.detourList;
            }

            public void Dispose() {
                curNode = null;
            }
        }
    }
}
