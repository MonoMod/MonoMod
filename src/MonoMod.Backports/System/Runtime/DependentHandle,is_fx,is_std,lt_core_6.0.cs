using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System.Runtime {
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types",
        Justification = "The BCL implementation doesn't, and this replicates its API.")]
    public struct DependentHandle : IDisposable {
        // The DependentHandle contract is really hard to replicate, as it turns out.
        // The contract is that the Key object is weakly held *even if strongly referenced by the value*.
        // It turns out, though, that through some indirection, and object resurrection, we *should* be able to
        // supply this contract correctly.

        // Target is the key, Dependent is the value.

        private GCHandle targetHandle;
        private GCHandle dependentHandle;
        private volatile bool allocated;

        // This object will be held with WeakTrackResurrection, so that it can resurrect itself
        // if the target is still alive.
        private sealed class DependentHolder {
            public GCHandle TargetHandle;
            public volatile object Dependent;

            public DependentHolder(GCHandle targetHandle, object dependent) {
                TargetHandle = targetHandle;
                Dependent = dependent;
            }

            ~DependentHolder() {
                // if the target still exists, resurrect ourselves.
                if (TargetHandle.IsAllocated)
                    GC.ReRegisterForFinalize(this);
            }
        }

        public DependentHandle(object? target, object? dependent) {
            // we allocate with WeakTrackResurrection so that a resurrected target doesn't
            // allow collection of the dependent
            targetHandle = GCHandle.Alloc(target, GCHandleType.WeakTrackResurrection);
            dependentHandle = AllocDepHolder(targetHandle, dependent);
            GC.KeepAlive(target);
            allocated = true;
        }

        private static GCHandle AllocDepHolder(GCHandle targetHandle, object? dependent) {
            var holder = dependent is not null ? new DependentHolder(targetHandle, dependent) : null;
            return GCHandle.Alloc(holder, GCHandleType.WeakTrackResurrection);
        }

        public bool IsAllocated => allocated;

        public object? Target {
            get {
                // TODO: how is the threadedness of this? what kind of synchronization do we need?
                if (!allocated)
                    throw new InvalidOperationException();
                return UnsafeGetTarget();
            }
            set {
                if (!allocated || value is not null)
                    throw new InvalidOperationException();
                UnsafeSetTargetToNull();
            }
        }

        public object? Dependent {
            get {
                if (!allocated)
                    throw new InvalidOperationException();
                return (dependentHandle.Target as DependentHolder)?.Dependent;
            }
            set {
                if (!allocated)
                    throw new InvalidOperationException();
                UnsafeSetDependent(value);
            }
        }

        public (object? Target, object? Dependent) TargetAndDependent {
            get {
                if (!allocated)
                    throw new InvalidOperationException();

                return (UnsafeGetTarget(), Dependent);
            }
        }

        internal object? UnsafeGetTarget() {
            return targetHandle.Target;
        }

        internal object? UnsafeGetTargetAndDependent(out object? dependent) {
            var target = UnsafeGetTarget();
            dependent = Dependent;
            return target;
        }

        internal void UnsafeSetTargetToNull() {
            Free();
        }

        internal void UnsafeSetDependent(object? value) {
            // we want to keep Target alive during this process
            var target = UnsafeGetTarget();

            if (target is null) {
                // if our target is dead, free everything and return
                Free();
                return;
            }

            if (value is not null) {
                if (dependentHandle.Target is DependentHolder holder) {
                    holder.Dependent = value;
                } else {
                    // the dependentHandle doesn't point to anything, we want to allocate a new handle
                    dependentHandle = AllocDepHolder(targetHandle, value);
                }
            } else {
                // if our new value is null, we just want to free the depholder
                FreeDependentHandle(dependentHandle);
            }

            GC.KeepAlive(target);
        }

        private static void FreeDependentHandle(GCHandle dependent) {
            if (dependent.Target is DependentHolder holder) {
                holder.TargetHandle.Free();
            }
            dependent.Free();
        }

        private void Free() {
            targetHandle.Free();
            FreeDependentHandle(dependentHandle);
        }

        public void Dispose() {
            allocated = false;
            Free();
        }
    }

}