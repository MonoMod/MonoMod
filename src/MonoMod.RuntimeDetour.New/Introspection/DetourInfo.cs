using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public sealed class DetourInfo : DetourBase {
        private readonly DetourManager.SingleDetourState detour;

        internal DetourInfo(MethodDetourInfo method, DetourManager.SingleDetourState detour) : base(method) {
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

        internal DetourManager.DetourChainNode? ChainNode
            => detour.ManagerData switch {
                DetourManager.DetourChainNode cn => cn,
                DetourManager.DepGraphNode<DetourManager.ChainNode> gn => (DetourManager.DetourChainNode) gn.ListNode.ChainNode,
                _ => null,
            };

        public DetourInfo? Next
            => ChainNode?.Next is DetourManager.DetourChainNode cn ? Method.GetDetourInfo(cn.Detour) : null;
    }
}
