using System.Diagnostics.CodeAnalysis;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Threading;

namespace System.Runtime
{
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types",
        Justification = "The BCL implementation doesn't, and this replicates its API.")]
    public struct DependentHandle : IDisposable
    {
        // The DependentHandle contract is really hard to replicate, as it turns out.
        // The contract is that the Key object is weakly held *even if strongly referenced by the value*.
        // It turns out, though, that through some indirection, and object resurrection, we *should* be able to
        // supply this contract correctly.

        // Target is the key, Dependent is the value.

        private GCHandle dependentHandle;
        private volatile bool allocated;

        // This object will be held with WeakTrackResurrection, so that it can resurrect itself
        // if the target is still alive.
        private sealed class DependentHolder : CriticalFinalizerObject
        {
            public GCHandle TargetHandle;
            private IntPtr dependent;

            // TODO: figure out a way to make this handle a normal object reference, but prevent finalization of
            // referenced object when we resurrect

            public object? Dependent
            {
                get => GCHandle.FromIntPtr(dependent).Target;
                set
                {
                    IntPtr oldHandle, newHandle = GCHandle.ToIntPtr(GCHandle.Alloc(value, GCHandleType.Normal));
                    do
                    {
                        oldHandle = dependent;
                    } while (Interlocked.CompareExchange(ref dependent, newHandle, oldHandle) == oldHandle);
                    GCHandle.FromIntPtr(oldHandle).Free();
                }
            }

            public DependentHolder(GCHandle targetHandle, object dependent)
            {
                TargetHandle = targetHandle;
                this.dependent = GCHandle.ToIntPtr(GCHandle.Alloc(dependent, GCHandleType.Normal));
            }

            ~DependentHolder()
            {
                // if the target still exists, resurrect ourselves and our dependent.
                if (!AppDomain.CurrentDomain.IsFinalizingForUnload() && !Environment.HasShutdownStarted
                    && TargetHandle.IsAllocated && TargetHandle.Target is not null)
                {
                    GC.ReRegisterForFinalize(this);
                }
                else
                {
                    GCHandle.FromIntPtr(dependent).Free();
                }
            }
        }

        public DependentHandle(object? target, object? dependent)
        {
            // we allocate with WeakTrackResurrection so that a resurrected target doesn't
            // allow collection of the dependent
            var targetHandle = GCHandle.Alloc(target, GCHandleType.WeakTrackResurrection);
            dependentHandle = AllocDepHolder(targetHandle, dependent);
            GC.KeepAlive(target);
            allocated = true;
        }

        private static GCHandle AllocDepHolder(GCHandle targetHandle, object? dependent)
        {
            var holder = dependent is not null ? new DependentHolder(targetHandle, dependent) : null;
            return GCHandle.Alloc(holder, GCHandleType.WeakTrackResurrection);
        }

        public bool IsAllocated => allocated;

        public object? Target
        {
            get
            {
                // TODO: how is the threadedness of this? what kind of synchronization do we need?
                if (!allocated)
                    throw new InvalidOperationException();
                return UnsafeGetTarget();
            }
            set
            {
                if (!allocated || value is not null)
                    throw new InvalidOperationException();
                UnsafeSetTargetToNull();
            }
        }

        public object? Dependent
        {
            get
            {
                if (!allocated)
                    throw new InvalidOperationException();
                return UnsafeGetHolder()?.Dependent;
            }
            set
            {
                if (!allocated)
                    throw new InvalidOperationException();
                UnsafeSetDependent(value);
            }
        }

        public (object? Target, object? Dependent) TargetAndDependent
        {
            get
            {
                if (!allocated)
                    throw new InvalidOperationException();

                return (UnsafeGetTarget(), Dependent);
            }
        }

        private DependentHolder? UnsafeGetHolder()
        {
            return Unsafe.As<DependentHolder?>(dependentHandle.Target);
        }

        internal object? UnsafeGetTarget()
        {
            return UnsafeGetHolder()?.TargetHandle.Target;
        }

        internal object? UnsafeGetTargetAndDependent(out object? dependent)
        {
            dependent = null;
            var holder = UnsafeGetHolder();
            if (holder is null)
            {
                return null;
            }
            var target = holder.TargetHandle.Target;
            if (target is null)
            {
                return null;
            }
            dependent = holder.Dependent;
            return target;
        }

        internal void UnsafeSetTargetToNull()
        {
            Free();
        }

        internal void UnsafeSetDependent(object? value)
        {
            var holder = UnsafeGetHolder();

            if (holder is null)
                return;

            if (!holder.TargetHandle.IsAllocated)
            {
                // if our target is dead, free everything and return
                Free();
                return;
            }

            holder.Dependent = value;
        }

        private void FreeDependentHandle()
        {
            if (allocated)
            {
                UnsafeGetHolder()?.TargetHandle.Free();
                dependentHandle.Free();
            }
            allocated = false;
        }

        private void Free()
        {
            FreeDependentHandle();
        }

        public void Dispose()
        {
            Free();
            allocated = false;
        }
    }

}