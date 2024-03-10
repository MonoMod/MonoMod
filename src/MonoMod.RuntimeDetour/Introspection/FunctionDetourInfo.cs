using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A type which represents a single native function, and permits access to the detours applied to that function.
    /// </summary>
    public sealed class FunctionDetourInfo
    {
        internal readonly DetourManager.NativeDetourState state;
        internal FunctionDetourInfo(DetourManager.NativeDetourState state)
        {
            this.state = state;
        }

        /// <summary>
        /// Gets a pointer to the function.
        /// </summary>
        public IntPtr Function => state.Function;

        /// <summary>
        /// Gets whether or not there are any active calls to this function.
        /// </summary>
        /// <remarks>
        /// This will only ever return true when there are detours applied.
        /// </remarks>
        public bool HasActiveCall => Volatile.Read(ref state.detourList.SyncInfo.ActiveCalls) > 0;

        private NativeDetourCollection? lazyDetours;
        /// <summary>
        /// Gets the detours applied to this function.
        /// </summary>
        public NativeDetourCollection Detours => lazyDetours ??= new(this);

        /// <summary>
        /// Gets the first detour in the detour chain.
        /// </summary>
        public NativeDetourInfo? FirstDetour
            => state.detourList.Next is DetourManager.NativeDetourChainNode cn ? GetDetourInfo(cn.Detour) : null;

        /// <summary>
        /// Gets whether or not this function is currently detoured.
        /// </summary>
        public bool IsDetoured => state.detourList.Next is not null;

        /// <summary>
        /// An event which is invoked whenever a detour is applied to this function.
        /// </summary>
        public event Action<NativeDetourInfo>? DetourApplied
        {
            add => state.NativeDetourApplied += value;
            remove => state.NativeDetourApplied -= value;
        }
        /// <summary>
        /// An event which is invoked whenver a detour is undone on this function.
        /// </summary>
        public event Action<NativeDetourInfo>? DetourUndone
        {
            add => state.NativeDetourUndone += value;
            remove => state.NativeDetourUndone -= value;
        }

        internal NativeDetourInfo GetDetourInfo(DetourManager.SingleNativeDetourState node)
        {
            var existingInfo = node.DetourInfo;
            if (existingInfo is null || existingInfo.Function != this)
            {
                return node.DetourInfo = new(this, node);
            }

            return existingInfo;
        }

        /// <summary>
        /// Takes the lock for this function. The detour chain will not be modified by other threads while this is held.
        /// </summary>
        /// <param name="lockTaken">A boolean which, when this method returns, holds whether or not the method took the
        /// lock and should release the lock in its <see langword="finally"/> block.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public void EnterLock(ref bool lockTaken)
        {
            state.detourLock.Enter(ref lockTaken);
        }

        /// <summary>
        /// Releases the lock for this function. Must be called only if <see cref="EnterLock(ref bool)"/>'s <c>lockTaken</c>
        /// was <see langword="true"/>
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public void ExitLock()
        {
            state.detourLock.Exit(true);
        }

        /// <summary>
        /// Takes the lock for this function, and returns a disposable object to automatically release the lock as needed
        /// in a <see langword="using"/> block.
        /// </summary>
        /// <returns>A disposable object which manages the lock.</returns>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public Lock WithLock() => new(this);

        /// <summary>
        /// A struct which is used to hold the function's lock.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible",
            Justification = "This being a nested type makes sense, as its basically only expected to be a temporary on-stack to " +
            "hold and automatically release a lock.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public readonly ref struct Lock
        {
            private readonly FunctionDetourInfo fdi;
            private readonly bool lockTaken;
            internal Lock(FunctionDetourInfo fdi)
            {
                this.fdi = fdi;
                lockTaken = false;
                try
                {
                    fdi.EnterLock(ref lockTaken);
                }
                catch
                {
                    if (lockTaken)
                        fdi.ExitLock();
                    throw;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (lockTaken)
                    fdi.ExitLock();
            }
        }
    }
}
