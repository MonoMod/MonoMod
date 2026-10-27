using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// Defers a group of <see cref="ILHook.Apply"/> operations and commits all
    /// hooks targeting the same method with a single IL-chain rebuild.
    /// </summary>
    /// <remarks>
    /// This is intended for controlled startup phases where many independent
    /// components install hooks before the target methods can run. Outside an
    /// active transaction, ILHook behavior is unchanged.
    /// </remarks>
    public sealed class ILHookTransaction : IDisposable
    {
        private sealed class OrderContext
        {
            public readonly long Order;
            public long Sequence;

            public OrderContext(long order)
            {
                Order = order;
            }
        }

        private sealed class PendingHook
        {
            public readonly ILHook Hook;
            public readonly long Order;
            public readonly long LocalSequence;
            public readonly long GlobalSequence;

            public PendingHook(ILHook hook, long order, long localSequence, long globalSequence)
            {
                Hook = hook;
                Order = order;
                LocalSequence = localSequence;
                GlobalSequence = globalSequence;
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly OrderContext? previous;
            private int disposed;

            public Scope(OrderContext? previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    currentOrder.Value = previous;
            }
        }

        private sealed class MonitorScope : IDisposable
        {
            private object? gate;

            public MonitorScope(object gate)
            {
                this.gate = gate;
                Monitor.Enter(gate);
            }

            public void Dispose()
            {
                var value = Interlocked.Exchange(ref gate, null);
                if (value is not null)
                    Monitor.Exit(value);
            }
        }

        private static readonly object transactionLock = new();
        private static readonly AsyncLocal<OrderContext?> currentOrder = new();
        private static readonly ConcurrentDictionary<ILHook, ILHookTransaction> pendingOwners = new();
        private static ILHookTransaction? activeTransaction;

        private readonly ConcurrentDictionary<ILHook, PendingHook> pending = new();
        private readonly ConcurrentDictionary<object, object> ownerGates = new();
        private readonly object unknownOwnerGate = new();
        private long globalSequence;
        private int state;

        private ILHookTransaction()
        {
        }

        /// <summary>
        /// Starts the process-wide IL-hook transaction.
        /// </summary>
        public static ILHookTransaction Begin()
        {
            lock (transactionLock)
            {
                if (activeTransaction is not null)
                    throw new InvalidOperationException("An ILHook transaction is already active");

                var transaction = new ILHookTransaction();
                Volatile.Write(ref activeTransaction, transaction);
                return transaction;
            }
        }

        /// <summary>
        /// Associates subsequently queued hooks on the current execution context
        /// with a stable ordering key. Hooks with the same key preserve call order.
        /// </summary>
        public static IDisposable EnterOrder(long order)
        {
            var previous = currentOrder.Value;
            currentOrder.Value = new OrderContext(order);
            return new Scope(previous);
        }

        /// <summary>
        /// Gets the number of hooks which are still waiting to be committed.
        /// </summary>
        public int PendingCount => pending.Count;

        internal static bool TryQueue(ILHook hook)
        {
            var transaction = Volatile.Read(ref activeTransaction);
            if (transaction is null || Volatile.Read(ref transaction.state) != 0)
                return false;

            var order = currentOrder.Value;
            var item = new PendingHook(
                hook,
                order?.Order ?? long.MaxValue,
                order is null ? 0 : Interlocked.Increment(ref order.Sequence),
                Interlocked.Increment(ref transaction.globalSequence)
            );

            if (!transaction.pending.TryAdd(hook, item))
                return true;
            if (!pendingOwners.TryAdd(hook, transaction))
            {
                transaction.pending.TryRemove(hook, out _);
                return false;
            }

            hook.SetTransactionPending(true);
            return true;
        }

        internal static bool TryCancel(ILHook hook)
        {
            if (!pendingOwners.TryGetValue(hook, out var transaction))
                return false;
            if (!transaction.pending.TryRemove(hook, out _))
                return false;

            pendingOwners.TryRemove(hook, out _);
            hook.SetTransactionPending(false);
            return true;
        }

        /// <summary>
        /// Commits every queued hook. Each target method is rebuilt only once.
        /// Target methods may be committed concurrently. Manipulators with the
        /// same owner key are always executed serially; different owners may run
        /// concurrently, while clone, generation and JIT work remain outside of
        /// the owner gate.
        /// </summary>
        /// <returns>The number of hooks and target methods committed.</returns>
        [CLSCompliant(false)]
        public (int Hooks, int Targets) Flush(int maxDegreeOfParallelism = 1,
            Func<MonoMod.Cil.ILContext.Manipulator, object?>? ownerSelector = null)
        {
            if (maxDegreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

            lock (transactionLock)
            {
                if (!ReferenceEquals(activeTransaction, this) || Interlocked.CompareExchange(ref state, 1, 0) != 0)
                    throw new InvalidOperationException("The ILHook transaction is not active");
                Volatile.Write(ref activeTransaction, null);
            }

            var snapshot = pending.Values
                .OrderBy(item => item.Order)
                .ThenBy(item => item.LocalSequence)
                .ThenBy(item => item.GlobalSequence)
                .ToArray();

            var groups = new List<List<PendingHook>>();
            var byState = new Dictionary<DetourManager.ManagedDetourState, List<PendingHook>>();
            foreach (var item in snapshot)
            {
                if (!byState.TryGetValue(item.Hook.ManagedState, out var group))
                {
                    group = new List<PendingHook>();
                    byState.Add(item.Hook.ManagedState, group);
                    groups.Add(group);
                }
                group.Add(item);
            }

            var hooksCommitted = 0;
            var targetsCommitted = 0;
            try
            {
                void CommitGroup(List<PendingHook> group)
                {
                    var hooks = new List<ILHook>(group.Count);
                    foreach (var item in group)
                    {
                        if (!pending.TryRemove(item.Hook, out _))
                            continue;
                        pendingOwners.TryRemove(item.Hook, out _);
                        hooks.Add(item.Hook);
                    }

                    if (hooks.Count == 0)
                        return;

                    try
                    {
                        hooks[0].ManagedState.AddILHooksBatch(
                            hooks.Select(hook => hook.HookState).ToArray(),
                            manipulator => EnterManipulatorGate(manipulator, ownerSelector)
                        );
                        Interlocked.Add(ref hooksCommitted, hooks.Count);
                        Interlocked.Increment(ref targetsCommitted);
                    }
                    finally
                    {
                        foreach (var hook in hooks)
                            hook.SetTransactionPending(false);
                    }
                }

                if (maxDegreeOfParallelism == 1)
                {
                    foreach (var group in groups)
                        CommitGroup(group);
                }
                else
                {
                    Parallel.ForEach(groups, new ParallelOptions {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism
                    }, CommitGroup);
                }

                Volatile.Write(ref state, 2);
                return (hooksCommitted, targetsCommitted);
            }
            finally
            {
                foreach (var item in pending.Keys)
                {
                    pendingOwners.TryRemove(item, out _);
                    item.SetTransactionPending(false);
                }
                pending.Clear();
                if (Volatile.Read(ref state) != 2)
                    Volatile.Write(ref state, 3);
            }
        }

        private IDisposable EnterManipulatorGate(MonoMod.Cil.ILContext.Manipulator manipulator,
            Func<MonoMod.Cil.ILContext.Manipulator, object?>? ownerSelector)
        {
            var owner = ownerSelector?.Invoke(manipulator) ?? manipulator.Method.DeclaringType?.Assembly;
            var gate = owner is null ? unknownOwnerGate : ownerGates.GetOrAdd(owner, static _ => new object());
            return new MonitorScope(gate);
        }

        /// <summary>
        /// Cancels any hooks which have not yet been committed.
        /// </summary>
        public void Dispose()
        {
            lock (transactionLock)
            {
                if (ReferenceEquals(activeTransaction, this))
                    Volatile.Write(ref activeTransaction, null);
            }

            if (Interlocked.CompareExchange(ref state, 3, 0) != 0)
                return;

            foreach (var hook in pending.Keys)
            {
                pendingOwners.TryRemove(hook, out _);
                hook.SetTransactionPending(false);
            }
            pending.Clear();
        }
    }
}
