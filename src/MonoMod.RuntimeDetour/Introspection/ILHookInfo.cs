using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// An object which represents an <see cref="ILHook"/> without extending its lifetime.
    /// </summary>
    public sealed class ILHookInfo : DetourBase
    {
        private readonly DetourManager.SingleILHookState hook;

        internal ILHookInfo(MethodDetourInfo method, DetourManager.SingleILHookState hook) : base(method)
        {
            this.hook = hook;
        }

        private protected override bool IsAppliedCore() => hook.IsApplied;
        private protected override DetourConfig? ConfigCore() => hook.Config;

        private protected override void ApplyCore()
        {
            if (hook.IsApplied)
            {
                throw new InvalidOperationException("ILHook is already applied");
            }

            if (!hook.IsValid)
            {
                throw new InvalidOperationException("ILHook is no longer valid");
            }

            Method.state.AddILHook(hook, false);
        }

        private protected override void UndoCore()
        {
            if (!hook.IsApplied)
            {
                throw new InvalidOperationException("ILHook is not currently applied");
            }

            Method.state.RemoveILHook(hook, false);
        }

        /// <summary>
        /// Gets the manipulator method used by this <see cref="ILHook"/>.
        /// </summary>
        public MethodInfo ManipulatorMethod => hook.Manip.Method;
    }
}
