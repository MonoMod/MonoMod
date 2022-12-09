using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public sealed class DetourInfo : DetourBase {
        private readonly DetourManager.SingleManagedDetourState detour;

        internal DetourInfo(MethodDetourInfo method, DetourManager.SingleManagedDetourState detour) : base(method) {
            this.detour = detour;
        }

        protected override bool IsAppliedCore() => detour.IsApplied;
        protected override DetourConfig? ConfigCore() => detour.Config;

        protected override void ApplyCore() {
            if (detour.IsApplied) {
                throw new InvalidOperationException("Detour is already applied");
            }

            if (!detour.IsValid) {
                throw new InvalidOperationException("Detour is no longer valid");
            }

            Method.state.AddDetour(detour, false);
        }

        protected override void UndoCore() {
            if (!detour.IsApplied) {
                throw new InvalidOperationException("Detour is not currently applied");
            }

            Method.state.RemoveDetour(detour, false);
        }

        public MethodBase Entry => detour.PublicTarget;

        internal DetourManager.ManagedDetourChainNode? ChainNode
            => detour.ManagerData switch {
                DetourManager.ManagedDetourChainNode cn => cn,
                DetourManager.DepGraphNode<DetourManager.ManagedChainNode> gn => (DetourManager.ManagedDetourChainNode) gn.ListNode.ChainNode,
                _ => null,
            };

        public DetourInfo? Next
            => ChainNode?.Next is DetourManager.ManagedDetourChainNode cn ? Method.GetDetourInfo(cn.Detour) : null;
    }
}
