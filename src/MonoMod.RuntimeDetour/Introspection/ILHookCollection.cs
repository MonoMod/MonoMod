using System;
using System.Collections;
using System.Collections.Generic;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A collection of <see cref="ILHookInfo"/> associated with a method.
    /// </summary>
    public sealed class ILHookCollection : IEnumerable<ILHookInfo>
    {
        private readonly MethodDetourInfo mdi;
        internal ILHookCollection(MethodDetourInfo mdi)
            => this.mdi = mdi;

        /// <summary>
        /// Gets an enumerator for this collection.
        /// </summary>
        /// <returns>An enumerator which enumterates the <see cref="ILHookInfo"/> associated with this collection.</returns>
        public Enumerator GetEnumerator() => new(mdi);

        IEnumerator<ILHookInfo> IEnumerable<ILHookInfo>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// An enumerator for an <see cref="ILHookCollection"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<ILHookInfo>
        {
            private readonly MethodDetourInfo mdi;
            private DetourManager.DepListNode<DetourManager.ILHookEntry>? listEntry;
            private List<DetourManager.ILHookEntry>.Enumerator listEnum;
            private int state;
            private int version;

            internal Enumerator(MethodDetourInfo mdi)
            {
                this.mdi = mdi;
                version = mdi.state.ilhookVersion;
                listEntry = null;
                state = 0;
                listEnum = default;
            }

            /// <inheritdoc/>
            public ILHookInfo Current
                => state switch
                {
                    0 => throw new InvalidOperationException(), // Current should never be called in state 0
                    1 => mdi.GetILHookInfo(listEntry!.ChainNode.Hook), // in state 1, our value is that of the current list node
                    2 => mdi.GetILHookInfo(listEnum.Current.Hook), // in state 2, our value is the current value of the list enumerator
                    _ => throw new InvalidOperationException() // all other states are invalid
                };

            object IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext()
            {
                if (version != mdi.state.ilhookVersion)
                    throw new InvalidOperationException("The detour chain was modified while enumerating");

                switch (state)
                {
                    case 0:
                        // we haven't started iterating yet
                        // start by grabbing the first entry
                        listEntry = mdi.state.ilhookGraph.ListHead;
                        state = 1;
                        goto CheckEnumeratingLL;

                    case 1:
                        // we're iterating the linked list, grab the next entry
                        listEntry = listEntry?.Next;
                        // state stays as 1
                        goto CheckEnumeratingLL;

                        CheckEnumeratingLL:
                        // we need to check the value of listEntry for null, and if it's null switch to enumerating the list enumerator
                        if (listEntry is not null)
                        {
                            // we have a list entry, we have a value to return
                            return true;
                        }

                        // we don't have a value, start list enumeration
                        listEnum = mdi.state.noConfigIlhooks.GetEnumerator();
                        state = 2;
                        goto case 2;

                    case 2:
                        // we're just enumerating the list, just need to call MoveNext
                        return listEnum.MoveNext();

                    default:
                        throw new InvalidOperationException("Invalid state");
                }
            }

            /// <inheritdoc/>
            public void Reset()
            {
                version = mdi.state.ilhookVersion;
                listEntry = null;
                state = 0;
                listEnum = default;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                listEnum.Dispose();
                Reset();
            }
        }
    }
}
