using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.RuntimeDetour {
    public sealed class DetourCollection : IEnumerable<DetourInfo> {
        private readonly MethodDetourInfo mdi;
        internal DetourCollection(MethodDetourInfo mdi)
            => this.mdi = mdi;

        public Enumerator GetEnumerator() => new(mdi);

        IEnumerator<DetourInfo> IEnumerable<DetourInfo>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<DetourInfo> {
            private readonly MethodDetourInfo mdi;
            private DetourManager.ChainNode? curNode;
            private int version;

            internal Enumerator(MethodDetourInfo mdi) {
                this.mdi = mdi;
                version = mdi.state.detourChainVersion;
                curNode = mdi.state.detourList;
            }

            public DetourInfo Current => mdi.GetDetourInfo(((DetourManager.DetourChainNode) curNode!).Detour);

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
