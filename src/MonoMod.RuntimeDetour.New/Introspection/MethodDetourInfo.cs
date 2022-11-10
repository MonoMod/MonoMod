using System;
using System.Reflection;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public sealed class MethodDetourInfo {
        internal readonly DetourManager.DetourState state;
        internal MethodDetourInfo(DetourManager.DetourState state) {
            this.state = state;
        }

        public MethodBase Method => state.Source;

        public bool HasActiveCall => Volatile.Read(ref state.detourList.SyncInfo.ActiveCalls) > 0;

        private DetourCollection? lazyDetours;
        public DetourCollection Detours => lazyDetours ??= new(this);

        private ILHookCollection? lazyILHooks;
        public ILHookCollection ILHooks => lazyILHooks ??= new(this);

        public DetourInfo? FirstDetour
            => state.detourList.Next is DetourManager.DetourChainNode cn ? GetDetourInfo(cn.Detour) : null;

        public bool IsDetoured => state.detourList.Next is not null || state.detourList.HasILHook;

        public event Action<DetourInfo>? DetourApplied {
            add => state.DetourApplied += value;
            remove => state.DetourApplied -= value;
        }
        public event Action<DetourInfo>? DetourUndone {
            add => state.DetourUndone += value;
            remove => state.DetourUndone -= value;
        }
        public event Action<ILHookInfo>? ILHookApplied {
            add => state.ILHookApplied += value;
            remove => state.ILHookApplied -= value;
        }
        public event Action<ILHookInfo>? ILHookUndone {
            add => state.ILHookUndone += value;
            remove => state.ILHookUndone -= value;
        }

        internal DetourInfo GetDetourInfo(DetourManager.SingleDetourState node) {
            var existingInfo = node.DetourInfo;
            if (existingInfo is null || existingInfo.Method!= this) {
                return node.DetourInfo = new(this, node);
            }

            return existingInfo;
        }

        internal ILHookInfo GetILHookInfo(DetourManager.SingleILHookState entry) {
            var existingInfo = entry.HookInfo;
            if (existingInfo is null || existingInfo.Method!= this) {
                return entry.HookInfo = new(this, entry);
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
            private readonly MethodDetourInfo mdi;
            private readonly bool lockTaken;
            internal Lock(MethodDetourInfo mdi) {
                this.mdi = mdi;
                lockTaken = false;
                try {
                    mdi.EnterLock(ref lockTaken);
                } catch {
                    if (lockTaken)
                        mdi.ExitLock();
                    throw;
                }
            }

            public void Dispose() {
                if (lockTaken)
                    mdi.ExitLock();
            }
        }
    }
}
