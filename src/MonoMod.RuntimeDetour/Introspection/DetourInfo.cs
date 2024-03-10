using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// An object which represents a detour, without extending its lifetime.
    /// </summary>
    public sealed class DetourInfo : DetourBase
    {
        private readonly DetourManager.SingleManagedDetourState detour;

        internal DetourInfo(MethodDetourInfo method, DetourManager.SingleManagedDetourState detour) : base(method)
        {
            this.detour = detour;
        }

        private protected override bool IsAppliedCore() => detour.IsApplied;
        private protected override DetourConfig? ConfigCore() => detour.Config;

        private protected override void ApplyCore()
        {
            if (detour.IsApplied)
            {
                throw new InvalidOperationException("Detour is already applied");
            }

            if (!detour.IsValid)
            {
                throw new InvalidOperationException("Detour is no longer valid");
            }

            Method.state.AddDetour(detour, false);
        }

        private protected override void UndoCore()
        {
            if (!detour.IsApplied)
            {
                throw new InvalidOperationException("Detour is not currently applied");
            }

            Method.state.RemoveDetour(detour, false);
        }

        /// <summary>
        /// Gets the entrypoint of the detour. This corresponds with <see cref="Hook.Target"/>.
        /// </summary>
        public MethodBase Entry => detour.PublicTarget;

        internal DetourManager.ManagedDetourChainNode? ChainNode
            => detour.ManagerData switch
            {
                DetourManager.ManagedDetourChainNode cn => cn,
                DetourManager.DepGraphNode<DetourManager.ManagedChainNode> gn => (DetourManager.ManagedDetourChainNode)gn.ListNode.ChainNode,
                _ => null,
            };

        /// <summary>
        /// Gets the next detour in the detour chain, if there is one.
        /// </summary>
        public DetourInfo? Next
            => ChainNode?.Next is DetourManager.ManagedDetourChainNode cn ? Method.GetDetourInfo(cn.Detour) : null;
    }
}
