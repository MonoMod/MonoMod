using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A collection of <see cref="DetourInfo"/> objects associated with a method.
    /// </summary>
    public sealed class DetourCollection : IEnumerable<DetourInfo>
    {
        private readonly MethodDetourInfo mdi;
        internal DetourCollection(MethodDetourInfo mdi)
            => this.mdi = mdi;

        /// <summary>
        /// Gets an enumerator for this collection.
        /// </summary>
        /// <returns>An enumerator which enumterates the <see cref="DetourInfo"/> associated with this collection.</returns>
        public Enumerator GetEnumerator() => new(mdi);

        IEnumerator<DetourInfo> IEnumerable<DetourInfo>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// An enumerator for a <see cref="DetourCollection"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<DetourInfo>
        {
            private readonly MethodDetourInfo mdi;
            private DetourManager.ManagedChainNode? curNode;
            private int version;

            internal Enumerator(MethodDetourInfo mdi)
            {
                this.mdi = mdi;
                version = mdi.state.detourChainVersion;
                curNode = mdi.state.detourList;
            }

            /// <inheritdoc/>
            public DetourInfo Current => mdi.GetDetourInfo(((DetourManager.ManagedDetourChainNode)curNode!).Detour);

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
