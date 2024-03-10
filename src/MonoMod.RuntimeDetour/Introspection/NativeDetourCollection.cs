using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A collection of <see cref="NativeDetourInfo"/> objects associated with a native function.
    /// </summary>
    public sealed class NativeDetourCollection : IEnumerable<NativeDetourInfo>
    {
        private readonly FunctionDetourInfo mdi;
        internal NativeDetourCollection(FunctionDetourInfo mdi)
            => this.mdi = mdi;

        /// <summary>
        /// Gets an enumerator for this collection.
        /// </summary>
        /// <returns>An enumerator which enumterates the <see cref="NativeDetourInfo"/> associated with this collection.</returns>
        public Enumerator GetEnumerator() => new(mdi);

        IEnumerator<NativeDetourInfo> IEnumerable<NativeDetourInfo>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// An enumerator for a <see cref="DetourCollection"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<NativeDetourInfo>
        {
            private readonly FunctionDetourInfo mdi;
            private DetourManager.NativeChainNode? curNode;
            private int version;

            internal Enumerator(FunctionDetourInfo mdi)
            {
                this.mdi = mdi;
                version = mdi.state.detourChainVersion;
                curNode = mdi.state.detourList;
            }

            /// <inheritdoc/>
            public NativeDetourInfo Current => mdi.GetDetourInfo(((DetourManager.NativeDetourChainNode)curNode!).Detour);

            object IEnumerator.Current => Current;

            /// <inheritdoc/>
            [MemberNotNullWhen(true, nameof(curNode))]
            public bool MoveNext()
            {
                if (version != mdi.state.detourChainVersion)
                    throw new InvalidOperationException("The detour chain was modified while enumerating");
                curNode = curNode?.Next;
                return curNode is not null;
            }

            /// <inheritdoc/>
            public void Reset()
            {
                version = mdi.state.detourChainVersion;
                curNode = mdi.state.detourList;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                curNode = null;
            }
        }
    }
}
