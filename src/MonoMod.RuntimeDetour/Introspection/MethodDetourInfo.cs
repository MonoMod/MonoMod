using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A type which represents a single method, and permits access to the detours applied to that method.
    /// </summary>
    public sealed class MethodDetourInfo
    {
        internal readonly DetourManager.ManagedDetourState state;
        internal MethodDetourInfo(DetourManager.ManagedDetourState state)
        {
            this.state = state;
        }

        /// <summary>
        /// Gets the method which this object represents.
        /// </summary>
        public MethodBase Method => state.Source;

        /// <summary>
        /// Gets whether or not there are any active calls to this function.
        /// </summary>
        /// <remarks>
        /// This will only ever return true when there are detours applied.
        /// </remarks>
        public bool HasActiveCall => Volatile.Read(ref state.detourList.SyncInfo.ActiveCalls) > 0;

        private DetourCollection? lazyDetours;
        /// <summary>
        /// Gets the detours applied to this function.
        /// </summary>
        public DetourCollection Detours => lazyDetours ??= new(this);

        private ILHookCollection? lazyILHooks;
        /// <summary>
        /// Gets the <see cref="ILHook"/>s applied to this function.
        /// </summary>
        public ILHookCollection ILHooks => lazyILHooks ??= new(this);

        /// <summary>
        /// Gets the first detour in the detour chain.
        /// </summary>
        public DetourInfo? FirstDetour
            => state.detourList.Next is DetourManager.ManagedDetourChainNode cn ? GetDetourInfo(cn.Detour) : null;

        /// <summary>
        /// Gets whether or not this function is currently detoured.
        /// </summary>
        public bool IsDetoured => state.detourList.Next is not null || state.detourList.HasILHook;

        /// <summary>
        /// An event which is invoked whenever a detour is applied to this function.
        /// </summary>
        public event Action<DetourInfo>? DetourApplied
        {
            add => state.DetourApplied += value;
            remove => state.DetourApplied -= value;
        }
        /// <summary>
        /// An event which is invoked whenver a detour is undone on this function.
        /// </summary>
        public event Action<DetourInfo>? DetourUndone
        {
            add => state.DetourUndone += value;
            remove => state.DetourUndone -= value;
        }
        /// <summary>
        /// An event which is invoked whenever an <see cref="ILHook"/> is applied to this function.
        /// </summary>
        public event Action<ILHookInfo>? ILHookApplied
        {
            add => state.ILHookApplied += value;
            remove => state.ILHookApplied -= value;
        }
        /// <summary>
        /// An event which is invoked whenver an <see cref="ILHook"/> is undone on this function.
        /// </summary>
        public event Action<ILHookInfo>? ILHookUndone
        {
            add => state.ILHookUndone += value;
            remove => state.ILHookUndone -= value;
        }

        internal DetourInfo GetDetourInfo(DetourManager.SingleManagedDetourState node)
        {
            var existingInfo = node.DetourInfo;
            if (existingInfo is null || existingInfo.Method != this)
            {
                return node.DetourInfo = new(this, node);
            }

            return existingInfo;
        }

        internal ILHookInfo GetILHookInfo(DetourManager.SingleILHookState entry)
        {
            var existingInfo = entry.HookInfo;
            if (existingInfo is null || existingInfo.Method != this)
            {
                return entry.HookInfo = new(this, entry);
            }

            return existingInfo;
        }

        /// <summary>
        /// Takes the lock for this method. The detour chain will not be modified by other threads while this is held.
        /// </summary>
        /// <param name="lockTaken">A boolean which, when this method returns, holds whether or not the method took the
        /// lock and should release the lock in its <see langword="finally"/> block.</param>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public void EnterLock(ref bool lockTaken)
        {
            state.detourLock.Enter(ref lockTaken);
        }

        /// <summary>
        /// Releases the lock for this method. Must be called only if <see cref="EnterLock(ref bool)"/>'s <c>lockTaken</c>
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
            private readonly MethodDetourInfo mdi;
            private readonly bool lockTaken;
            internal Lock(MethodDetourInfo mdi)
            {
                this.mdi = mdi;
                lockTaken = false;
                try
                {
                    mdi.EnterLock(ref lockTaken);
                }
                catch
                {
                    if (lockTaken)
                        mdi.ExitLock();
                    throw;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (lockTaken)
                    mdi.ExitLock();
            }
        }
    }
}
