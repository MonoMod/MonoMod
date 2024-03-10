namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// The base class for detours associated with a <see cref="MethodDetourInfo"/>.
    /// </summary>
    public abstract class DetourBase
    {
        /// <summary>
        /// Gets the <see cref="MethodDetourInfo"/> this detour is associated with.
        /// </summary>
        public MethodDetourInfo Method { get; }

        private protected DetourBase(MethodDetourInfo method)
            => Method = method;

        private protected abstract bool IsAppliedCore();
        private protected abstract DetourConfig? ConfigCore();

        /// <summary>
        /// Gets whether or not this detour is applied.
        /// </summary>
        public bool IsApplied => IsAppliedCore();
        /// <summary>
        /// Gets the config associated with this detour, if any.
        /// </summary>
        public DetourConfig? Config => ConfigCore();

        // I'm still not sure if I'm happy with this being publicly exposed...

        /// <summary>
        /// Applies this detour.
        /// </summary>
        public void Apply()
        {
            ref var spinLock = ref Method.state.detourLock;
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
            ref var spinLock = ref Method.state.detourLock;
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

        private protected abstract void ApplyCore();
        private protected abstract void UndoCore();
    }
}
