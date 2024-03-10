using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// An object which represents a native detour, without extending its lifetime.
    /// </summary>
    public sealed class NativeDetourInfo
    {
        private readonly DetourManager.SingleNativeDetourState detour;

        internal NativeDetourInfo(FunctionDetourInfo fdi, DetourManager.SingleNativeDetourState detour)
        {
            Function = fdi;
            this.detour = detour;
        }

        /// <summary>
        /// Gets the <see cref="FunctionDetourInfo"/> for the function this detour is attached to.
        /// </summary>
        public FunctionDetourInfo Function { get; }

        /// <summary>
        /// Gets whether or not this detour is currently applied.
        /// </summary>
        public bool IsApplied => detour.IsApplied;
        /// <summary>
        /// Gets the config associated with this detour, if any.
        /// </summary>
        public DetourConfig? Config => detour.Config;

        // I'm still not sure if I'm happy with this being publicly exposed...

        /// <summary>
        /// Applies this detour.
        /// </summary>
        public void Apply()
        {
            ref var spinLock = ref Function.state.detourLock;
            var lockTaken = spinLock.IsThreadOwnerTrackingEnabled && spinLock.IsHeldByCurrentThread;
            try
            {
                if (!lockTaken)
                    spinLock.Enter(ref lockTaken);

                ApplyCore();
            }
            finally
            {
                if (lockTaken)
                    spinLock.Exit(true);
            }
        }

        /// <summary>
        /// Undoes this detour.
        /// </summary>
        public void Undo()
        {
            ref var spinLock = ref Function.state.detourLock;
            var lockTaken = spinLock.IsThreadOwnerTrackingEnabled && spinLock.IsHeldByCurrentThread;
            try
            {
                if (!lockTaken)
                    spinLock.Enter(ref lockTaken);

                UndoCore();
            }
            finally
            {
                if (lockTaken)
                    spinLock.Exit(true);
            }
        }

        private void ApplyCore()
        {
            if (detour.IsApplied)
            {
                throw new InvalidOperationException("NativeDetour is already applied");
            }

            if (!detour.IsValid)
            {
                throw new InvalidOperationException("NativeDetour is no longer valid");
            }

            Function.state.AddDetour(detour, false);
        }

        private void UndoCore()
        {
            if (!detour.IsApplied)
            {
                throw new InvalidOperationException("NativeDetour is not currently applied");
            }

            Function.state.RemoveDetour(detour, false);
        }

        /// <summary>
        /// Gets the entrypoint of the detour. This is the method which implements the delegate passed into <see cref="NativeHook"/>.
        /// </summary>
        public MethodInfo Entry => detour.Invoker.Method;

        internal DetourManager.NativeDetourChainNode? ChainNode
            => detour.ManagerData switch
            {
                DetourManager.NativeDetourChainNode cn => cn,
                DetourManager.DepGraphNode<DetourManager.NativeChainNode> gn => (DetourManager.NativeDetourChainNode)gn.ListNode.ChainNode,
                _ => null,
            };

        /// <summary>
        /// Gets the next detour in the detour chain, if any.
        /// </summary>
        public NativeDetourInfo? Next
            => ChainNode?.Next is DetourManager.NativeDetourChainNode cn ? Function.GetDetourInfo(cn.Detour) : null;
    }
}
