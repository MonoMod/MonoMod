using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public sealed class ILHookInfo : DetourBase {
        private readonly DetourManager.SingleILHookState hook;

        internal ILHookInfo(MethodDetourInfo method, DetourManager.SingleILHookState hook) : base(method) {
            this.hook = hook;
        }

        protected override bool IsAppliedCore() => hook.IsApplied;
        protected override DetourConfig? ConfigCore() => hook.Config;

        protected override void ApplyCore() {
            if (hook.IsApplied) {
                throw new InvalidOperationException("ILHook is already applied");
            }

            if (!hook.IsValid) {
                throw new InvalidOperationException("ILHook is no longer valid");
            }

            Method.state.AddILHook(hook, false);
        }

        protected override void UndoCore() {
            if (!hook.IsApplied) {
                throw new InvalidOperationException("ILHook is not currently applied");
            }

            Method.state.RemoveILHook(hook, false);
        }

        public MethodInfo ManipulatorMethod => hook.Manip.Method;
    }
}
