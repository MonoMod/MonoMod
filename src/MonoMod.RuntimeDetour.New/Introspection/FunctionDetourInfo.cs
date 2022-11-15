using System;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public sealed class FunctionDetourInfo {
        internal readonly DetourManager.NativeDetourState state;
        internal FunctionDetourInfo(DetourManager.NativeDetourState state) {
            this.state = state;
        }

        public IntPtr Function => state.Function;

        public bool HasActiveCall => Volatile.Read(ref state.detourList.SyncInfo.ActiveCalls) > 0;

        private NativeDetourCollection? lazyDetours;
        public NativeDetourCollection Detours => lazyDetours ??= new(this);

        public NativeDetourInfo? FirstDetour
            => state.detourList.Next is DetourManager.NativeDetourChainNode cn ? GetDetourInfo(cn.Detour) : null;

        public bool IsDetoured => state.detourList.Next is not null;

        public event Action<NativeDetourInfo>? DetourApplied {
            add => state.NativeDetourApplied += value;
            remove => state.NativeDetourApplied -= value;
        }
        public event Action<NativeDetourInfo>? DetourUndone {
            add => state.NativeDetourUndone += value;
            remove => state.NativeDetourUndone -= value;
        }
        internal NativeDetourInfo GetDetourInfo(DetourManager.SingleNativeDetourState node) {
            var existingInfo = node.DetourInfo;
            if (existingInfo is null || existingInfo.Function != this) {
                return node.DetourInfo = new(this, node);
            }

            return existingInfo;
        }

        public void EnterLock(ref bool lockTaken) {
            state.detourLock.Enter(ref lockTaken);
        }

        public void ExitLock() {
            state.detourLock.Exit(true);
        }

        public Lock WithLock() => new(this);

        public ref struct Lock {
            private readonly FunctionDetourInfo fdi;
            private readonly bool lockTaken;
            internal Lock(FunctionDetourInfo fdi) {
                this.fdi = fdi;
                lockTaken = false;
                try {
                    fdi.EnterLock(ref lockTaken);
                } catch {
                    if (lockTaken)
                        fdi.ExitLock();
                    throw;
                }
            }

            public void Dispose() {
                if (lockTaken)
                    fdi.ExitLock();
            }
        }
    }
}
