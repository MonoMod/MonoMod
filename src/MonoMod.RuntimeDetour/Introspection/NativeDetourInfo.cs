using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public sealed class NativeDetourInfo {
        private readonly DetourManager.SingleNativeDetourState detour;

        internal NativeDetourInfo(FunctionDetourInfo fdi, DetourManager.SingleNativeDetourState detour) {
            Function = fdi;
            this.detour = detour;
        }

        public FunctionDetourInfo Function { get; }

        public bool IsApplied => detour.IsApplied;
        public DetourConfig? Config => detour.Config;

        // I'm still not sure if I'm happy with this being publicly exposed...

        public void Apply() {
            ref var spinLock = ref Function.state.detourLock;
            var lockTaken = spinLock.IsThreadOwnerTrackingEnabled && spinLock.IsHeldByCurrentThread;
            try {
                if (!lockTaken)
                    spinLock.Enter(ref lockTaken);

                ApplyCore();
            } finally {
                if (lockTaken)
                    spinLock.Exit(true);
            }
        }

        public void Undo() {
            ref var spinLock = ref Function.state.detourLock;
            var lockTaken = spinLock.IsThreadOwnerTrackingEnabled && spinLock.IsHeldByCurrentThread;
            try {
                if (!lockTaken)
                    spinLock.Enter(ref lockTaken);

                UndoCore();
            } finally {
                if (lockTaken)
                    spinLock.Exit(true);
            }
        }

        private void ApplyCore() {
            if (detour.IsApplied) {
                throw new InvalidOperationException("NativeDetour is already applied");
            }

            if (!detour.IsValid) {
                throw new InvalidOperationException("NativeDetour is no longer valid");
            }

            Function.state.AddDetour(detour, false);
        }

        private void UndoCore() {
            if (!detour.IsApplied) {
                throw new InvalidOperationException("NativeDetour is not currently applied");
            }

            Function.state.RemoveDetour(detour, false);
        }

        public MethodInfo Entry => detour.Invoker.Method;

        internal DetourManager.NativeDetourChainNode? ChainNode
            => detour.ManagerData switch {
                DetourManager.NativeDetourChainNode cn => cn,
                DetourManager.DepGraphNode<DetourManager.NativeChainNode> gn => (DetourManager.NativeDetourChainNode) gn.ListNode.ChainNode,
                _ => null,
            };

        public NativeDetourInfo? Next
            => ChainNode?.Next is DetourManager.NativeDetourChainNode cn ? Function.GetDetourInfo(cn.Detour) : null;
    }
}
