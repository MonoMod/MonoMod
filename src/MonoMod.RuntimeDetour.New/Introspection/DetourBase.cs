namespace MonoMod.RuntimeDetour {
    public abstract class DetourBase {
        public MethodDetourInfo Method { get; }

        private protected DetourBase(MethodDetourInfo method)
            => Method = method;

        protected abstract bool IsAppliedCore();
        protected abstract DetourConfig? ConfigCore();

        public bool IsApplied => IsAppliedCore();
        public DetourConfig? Config => ConfigCore();

        // I'm still not sure if I'm happy with this being publicly exposed...

        public void Apply() {
            ref var spinLock = ref Method.state.detourLock;
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
            ref var spinLock = ref Method.state.detourLock;
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

        protected abstract void ApplyCore();
        protected abstract void UndoCore();
    }
}
