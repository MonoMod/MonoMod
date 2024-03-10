using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace MonoMod.RuntimeDetour
{
    public static partial class DetourManager
    {
        #region Detour chain
        internal abstract class ManagedChainNode
        {

            public ManagedChainNode? Next;

            public abstract MethodBase Entry { get; }
            public abstract MethodBase NextTrampoline { get; }
            public abstract DetourConfig? Config { get; }
            public virtual bool DetourToFallback => true;

            private MethodBase? lastTarget;
            private ICoreDetour? trampolineDetour;
            private bool hasStolenTrampoline;

            public bool IsApplied { get; private set; }

            private void UndoTrampolineDetour()
            {
                var detour = Interlocked.Exchange(ref trampolineDetour, null);
                if (detour is not null)
                {
                    detour.Undo();
                    // TODO: cache trampolineDetours for a time, so they can be reused
                    detour.Dispose();
                }
            }

            public virtual void UpdateDetour(IDetourFactory factory, MethodBase fallback)
            {
                var to = Next?.Entry;
                if (to is null && DetourToFallback)
                {
                    to = fallback;
                }

                if (to == lastTarget)
                {
                    // our target hasn't changed, don't need to update this link
                    return;
                }

                UndoTrampolineDetour();

                if (to is not null)
                {
                    trampolineDetour = factory.CreateDetour(NextTrampoline, to, applyByDefault: true);
                }

                lastTarget = to;
                IsApplied = true;
            }

            public void Remove()
            {
                if (!hasStolenTrampoline)
                {
                    UndoTrampolineDetour();
                }
                lastTarget = null;
                Next = null;
                IsApplied = false;
            }

            public void StealTrampoline(IDetourFactory factory)
            {
                Helpers.Assert(!hasStolenTrampoline);

                StealTrampolineInner();
                hasStolenTrampoline = true;

                UndoTrampolineDetour();
                trampolineDetour = factory.CreateDetour(NextTrampoline, GetRemovedStub(MethodSignature.ForMethod(NextTrampoline)), applyByDefault: true);
            }
            protected virtual void StealTrampolineInner() => throw new NotSupportedException("Can't steal ManagedChainNode trampoline");

            public virtual void ReturnStolenTrampoline()
            {
                Helpers.Assert(hasStolenTrampoline);

                UndoTrampolineDetour();

                ReturnStolenTrampolineInner();
                hasStolenTrampoline = false;
            }
            protected virtual void ReturnStolenTrampolineInner() => throw new NotSupportedException("Can't steal ManagedChainNode trampoline");

        }

        internal sealed class ManagedDetourChainNode : ManagedChainNode
        {
            public ManagedDetourChainNode(SingleManagedDetourState detour)
            {
                Detour = detour;
            }

            public readonly SingleManagedDetourState Detour;

            public override MethodBase Entry => Detour.InvokeTarget;
            public override MethodBase NextTrampoline => Detour.NextTrampoline.TrampolineMethod;
            public override DetourConfig? Config => Detour.Config;
            public IDetourFactory Factory => Detour.Factory;

            protected override void StealTrampolineInner() => Detour.NextTrampoline.StealTrampolineOwnership();
            protected override void ReturnStolenTrampolineInner() => Detour.NextTrampoline.ReturnTrampolineOwnership();
        }

        internal sealed class ManagedDetourSyncInfo : DetourSyncInfo
        {

            public int HasStolenTrampolines;
            public readonly ConcurrentQueue<ManagedChainNode> TrampolineStealers = new ConcurrentQueue<ManagedChainNode>();

            public void StealTrampoline(IDetourFactory factory, ManagedChainNode node)
            {
                node.StealTrampoline(factory);

                // We don't have a race condition with ReturnStolenTrampolines here because:
                // 1. there is at least one active call by this thread whenever we steal a trampoline
                // 2. we wait for all other threads to have returned from the method before stealing the trampoline
                // -> these threads can't end up in ReturnStolenTrampolines because of 1.
                TrampolineStealers.Enqueue(node);
                Volatile.Write(ref HasStolenTrampolines, 1);
            }

            public void ReturnStolenTrampolines()
            {
                if (Interlocked.CompareExchange(ref HasStolenTrampolines, 0, 1) != 1)
                {
                    return;
                }

                while (TrampolineStealers.TryDequeue(out var node))
                {
                    node.ReturnStolenTrampoline();
                }
            }

        }

        private static readonly MethodInfo ManagedDetourSyncInfo_ReturnStolenTrampolines = typeof(ManagedDetourSyncInfo).GetMethod(nameof(ManagedDetourSyncInfo.ReturnStolenTrampolines))!;

        // The root node is the existing method. It's NextTrampoline is the method, which is the same
        // as the entry point, because we want to detour the entry point. Entry should never be targeted though.
        internal sealed class RootManagedChainNode : ManagedChainNode
        {
            public override MethodBase Entry { get; }
            public override MethodBase NextTrampoline { get; }
            public override DetourConfig? Config => null;
            public override bool DetourToFallback => true; // we do want to detour to fallback, because our sync proxy might be waiting to call the method

            public readonly MethodSignature Sig;
            public readonly ManagedDetourSyncInfo SyncInfo = new();
            public readonly ConcurrentQueue<Action> StolenTrampolineReturners = new ConcurrentQueue<Action>();
            private readonly DataScope<DynamicReferenceCell> syncProxyRefScope;

            public bool HasILHook;

            public RootManagedChainNode(MethodBase method)
            {
                Sig = MethodSignature.ForMethod(method);
                Entry = method;
                NextTrampoline = TrampolinePool.Rent(Sig);

                DataScope<DynamicReferenceCell> refScope = default;
                SyncInfo.SyncProxy = GenerateSyncProxy(DebugFormatter.Format($"{Entry}"), Sig,
                    (method, il) => refScope = il.EmitNewTypedReference(SyncInfo, out _),
                    (method, il, loadSyncInfo) =>
                    {
                        foreach (var p in method.Parameters)
                        {
                            il.Emit(OpCodes.Ldarg, p);
                        }
                        il.Emit(OpCodes.Call, method.Module.ImportReference(NextTrampoline));
                    },
                    (method, il, loadSyncInfo) =>
                    {
                        // we keep the stolen trampolines alive a bit longer than required by only returning them once *all* threads have returned from the method
                        // but doing it this way avoids an expensive TLV lookup to track per-thread active calls
                        loadSyncInfo();
                        il.Emit(OpCodes.Call, ManagedDetourSyncInfo_ReturnStolenTrampolines);
                    });
                syncProxyRefScope = refScope;
            }

            private ICoreDetour? syncDetour;

            public override void UpdateDetour(IDetourFactory factory, MethodBase fallback)
            {
                base.UpdateDetour(factory, fallback);

                syncDetour ??= factory.CreateDetour(Entry, SyncInfo.SyncProxy!, applyByDefault: false);

                if (!HasILHook && Next is null && syncDetour.IsApplied)
                {
                    syncDetour.Undo();
                }
                else if ((HasILHook || Next is not null) && !syncDetour.IsApplied)
                {
                    syncDetour.Apply();
                }
            }
        }
        #endregion

        #region ILHook chain
        internal sealed class ILHookEntry
        {
            public readonly SingleILHookState Hook;

            public IDetourFactory Factory => Hook.Factory;
            public DetourConfig? Config => Hook.Config;
            public ILContext.Manipulator Manip => Hook.Manip;
            public ILContext? CurrentContext;
            public ILContext? LastContext;
            public bool IsApplied;

            public ILHookEntry(SingleILHookState hook)
            {
                Hook = hook;
            }

            public void Remove()
            {
                IsApplied = false;
                LastContext?.Dispose();
            }
        }
        #endregion

        internal sealed class ManagedDetourState
        {
            public readonly MethodBase Source;
            public readonly MethodBase ILCopy;
            public MethodBase EndOfChain;

            public ManagedDetourState(MethodBase src)
            {
                Source = src;
                ILCopy = src.CreateILCopy();
                EndOfChain = ILCopy;
                detourList = new(src);
            }

            private MethodDetourInfo? info;
            public MethodDetourInfo Info => info ??= new(this);

            private readonly DepGraph<ManagedChainNode> detourGraph = new();
            internal readonly RootManagedChainNode detourList;
            private ManagedChainNode? noConfigChain;

            internal SpinLock detourLock = new(true);
            internal int detourChainVersion;

            public void AddDetour(SingleManagedDetourState detour, bool takeLock = true)
            {
                ManagedDetourChainNode cnode;
                var lockTaken = false;
                try
                {
                    if (takeLock)
                        detourLock.Enter(ref lockTaken);
                    if (detour.ManagerData is not null)
                        throw new InvalidOperationException("Trying to add a detour which was already added");

                    cnode = new ManagedDetourChainNode(detour);
                    detourChainVersion++;
                    if (cnode.Config is { } cfg)
                    {
                        var listNode = new DepListNode<ManagedChainNode>(cfg, cnode);
                        var graphNode = new DepGraphNode<ManagedChainNode>(listNode);

                        detourGraph.Insert(graphNode);

                        detour.ManagerData = graphNode;
                    }
                    else
                    {
                        cnode.Next = noConfigChain;
                        noConfigChain = cnode;

                        detour.ManagerData = cnode;
                    }

                    UpdateChain(detour.Factory, out _);
                }
                finally
                {
                    if (lockTaken)
                        detourLock.Exit(true);
                }

                // TODO: make sure this ACTUALLY called outside of the lock
                InvokeDetourEvent(DetourManager.DetourApplied, DetourApplied, detour);
            }

            public void RemoveDetour(SingleManagedDetourState detour, bool takeLock = true)
            {
                ManagedDetourChainNode cnode;
                var lockTaken = false;
                try
                {
                    if (takeLock)
                        detourLock.Enter(ref lockTaken);
                    detourChainVersion++;
                    switch (Interlocked.Exchange(ref detour.ManagerData, null))
                    {
                        case null:
                            throw new InvalidOperationException("Trying to remove detour which wasn't added");

                        case DepGraphNode<ManagedChainNode> gn:
                            RemoveGraphDetour(detour, gn);
                            cnode = (ManagedDetourChainNode)gn.ListNode.ChainNode;
                            break;

                        case ManagedDetourChainNode cn:
                            RemoveNoConfigDetour(detour, cn);
                            cnode = cn;
                            break;

                        default:
                            throw new InvalidOperationException("Trying to remove detour with unknown manager data");
                    }
                }
                finally
                {
                    if (lockTaken)
                        detourLock.Exit(true);
                }

                // TODO: make sure this ACTUALLY called outside of the lock
                InvokeDetourEvent(DetourManager.DetourUndone, DetourUndone, detour);
            }

            private void RemoveGraphDetour(SingleManagedDetourState detour, DepGraphNode<ManagedChainNode> node)
            {
                detourGraph.Remove(node);
                UpdateChain(detour.Factory, out var stealTrampoline);
                if (stealTrampoline)
                {
                    detourList.SyncInfo.StealTrampoline(detour.Factory, node.ListNode.ChainNode);
                }
                node.ListNode.ChainNode.Remove();
            }

            private void RemoveNoConfigDetour(SingleManagedDetourState detour, ManagedDetourChainNode node)
            {
                ref var chain = ref noConfigChain;
                while (chain is not null)
                {
                    if (ReferenceEquals(chain, node))
                    {
                        chain = node.Next;
                        node.Next = null;
                        break;
                    }

                    chain = ref chain.Next;
                }

                UpdateChain(detour.Factory, out var stealTrampoline);
                if (stealTrampoline)
                {
                    detourList.SyncInfo.StealTrampoline(detour.Factory, node);
                }
                node.Remove();
            }

            internal readonly DepGraph<ILHookEntry> ilhookGraph = new();
            internal readonly List<ILHookEntry> noConfigIlhooks = new();

            internal int ilhookVersion;
            public void AddILHook(SingleILHookState ilhook, bool takeLock = true)
            {
                ILHookEntry entry;
                var lockTaken = false;
                try
                {
                    if (takeLock)
                        detourLock.Enter(ref lockTaken);
                    if (ilhook.ManagerData is not null)
                        throw new InvalidOperationException("Trying to add an IL hook which was already added");

                    entry = new ILHookEntry(ilhook);
                    ilhookVersion++;
                    if (entry.Config is { } cfg)
                    {
                        var listNode = new DepListNode<ILHookEntry>(cfg, entry);
                        var graphNode = new DepGraphNode<ILHookEntry>(listNode);

                        ilhookGraph.Insert(graphNode);

                        ilhook.ManagerData = graphNode;
                    }
                    else
                    {
                        noConfigIlhooks.Add(entry);
                        ilhook.ManagerData = entry;
                    }

                    try
                    {
                        UpdateEndOfChain();
                    }
                    catch
                    {
                        // the add failed, remove the node and re-update end of chain
                        switch (Interlocked.Exchange(ref ilhook.ManagerData, null))
                        {
                            case DepGraphNode<ILHookEntry> gn:
                                ilhookGraph.Remove(gn);
                                break;
                            case ILHookEntry cn:
                                noConfigIlhooks.Remove(cn);
                                break;
                            default:
                                throw new NotSupportedException("bad managerdata?");
                        }
                        UpdateEndOfChain();
                        throw;
                    }
                    UpdateChain(ilhook.Factory, out _);
                }
                finally
                {
                    if (lockTaken)
                        detourLock.Exit(true);
                }

                // TODO: make sure this ACTUALLY called outside of the lock
                InvokeILHookEvent(DetourManager.ILHookApplied, ILHookApplied, ilhook);
            }

            public void RemoveILHook(SingleILHookState ilhook, bool takeLock = true)
            {
                ILHookEntry entry;
                var lockTaken = false;
                try
                {
                    if (takeLock)
                        detourLock.Enter(ref lockTaken);
                    ilhookVersion++;
                    switch (Interlocked.Exchange(ref ilhook.ManagerData, null))
                    {
                        case null:
                            throw new InvalidOperationException("Trying to remove IL hook which wasn't added");

                        case DepGraphNode<ILHookEntry> gn:
                            RemoveGraphILHook(ilhook, gn);
                            entry = gn.ListNode.ChainNode;
                            break;

                        case ILHookEntry cn:
                            RemoveNoConfigILHook(ilhook, cn);
                            entry = cn;
                            break;

                        default:
                            throw new InvalidOperationException("Trying to remove IL hook with unknown manager data");
                    }
                }
                finally
                {
                    if (lockTaken)
                        detourLock.Exit(true);
                }

                // TODO: make sure this ACTUALLY called outside of the lock
                InvokeILHookEvent(DetourManager.ILHookUndone, ILHookUndone, ilhook);
            }

            private void RemoveGraphILHook(SingleILHookState ilhook, DepGraphNode<ILHookEntry> node)
            {
                ilhookGraph.Remove(node);
                UpdateEndOfChain();
                UpdateChain(ilhook.Factory, out _);
                CleanILContexts();
                node.ListNode.ChainNode.Remove();
            }

            private void RemoveNoConfigILHook(SingleILHookState ilhook, ILHookEntry node)
            {
                noConfigIlhooks.Remove(node);
                UpdateEndOfChain();
                UpdateChain(ilhook.Factory, out _);
                CleanILContexts();
                node.Remove();
            }

            private void UpdateEndOfChain()
            {
                if (noConfigIlhooks.Count == 0 && ilhookGraph.ListHead is null)
                {
                    detourList.HasILHook = false;
                    EndOfChain = ILCopy;
                    return;
                }

                detourList.HasILHook = true;

                using var dmd = new DynamicMethodDefinition(Source);

                var def = dmd.Definition!;
                var cur = ilhookGraph.ListHead;
                while (cur is not null)
                {
                    InvokeManipulator(cur.ChainNode, def);
                    cur = cur.Next;
                }

                foreach (var node in noConfigIlhooks)
                {
                    InvokeManipulator(node, def);
                }

                var eoc = dmd.Generate();

                // compile the method in-band to throw for invalid code here
                PlatformTriple.Current.Compile(eoc);

                // don't set EndOfChain until after the method successfully compiles, to ensure some semblance of consistenfy
                Thread.MemoryBarrier();
                EndOfChain = eoc;
            }

            private static void InvokeManipulator(ILHookEntry entry, MethodDefinition def)
            {
                //entry.LastContext?.Dispose(); // we can't safely clean up the old context until after we've updated the chain to point at the new method
                entry.IsApplied = true;
                var il = new ILContext(def);
                entry.CurrentContext = il;
                il.Invoke(entry.Manip);
                if (il.IsReadOnly)
                {
                    il.Dispose();
                    return;
                }

                // Free the now useless MethodDefinition and ILProcessor references.
                // This also prevents clueless people from storing the ILContext elsewhere
                // and reusing it outside of the IL manipulation context.
                il.MakeReadOnly();
                return;
            }

            private void CleanILContexts()
            {
                var cur = ilhookGraph.ListHead;
                while (cur is not null)
                {
                    CleanContext(cur.ChainNode);
                    cur = cur.Next;
                }

                foreach (var node in noConfigIlhooks)
                {
                    CleanContext(node);
                }

                static void CleanContext(ILHookEntry entry)
                {
                    if (entry.CurrentContext == entry.LastContext)
                        return;
                    var old = entry.LastContext;
                    entry.LastContext = entry.CurrentContext;
                    old?.Dispose();
                }
            }

            private void UpdateChain(IDetourFactory updatingFactory, out bool stealTrampolines)
            {
                var graphNode = detourGraph.ListHead;

                ManagedChainNode? chain = null;
                ref var next = ref chain;
                while (graphNode is not null)
                {
                    next = graphNode.ChainNode;
                    next = ref next.Next;
                    next = null; // clear it to be safe before continuing
                    graphNode = graphNode.Next;
                }

                // after building the chain from the graph list, add the noConfigChain
                next = noConfigChain;

                // our chain is now fully built, with the head in chain
                detourList.Next = chain; // detourList is the head of the real chain, and represents the original method

                Volatile.Write(ref detourList.SyncInfo.UpdatingThread, EnvironmentEx.CurrentManagedThreadId);
                detourList.SyncInfo.WaitForNoActiveCalls(out stealTrampolines);
                try
                {
                    chain = detourList;
                    while (chain is not null)
                    {
                        // we want to use the factory for the next node first
                        var fac = (chain.Next as ManagedDetourChainNode)?.Factory;
                        // then, if that doesn't exist, the current factory
                        fac ??= (chain as ManagedDetourChainNode)?.Factory;
                        // and if that doesn't exist, then the updating factory
                        fac ??= updatingFactory;
                        chain.UpdateDetour(fac, EndOfChain);

                        chain = chain.Next;
                    }
                }
                finally
                {
                    Volatile.Write(ref detourList.SyncInfo.UpdatingThread, -1);
                }
            }

            public event Action<DetourInfo>? DetourApplied;
            public event Action<DetourInfo>? DetourUndone;
            public event Action<ILHookInfo>? ILHookApplied;
            public event Action<ILHookInfo>? ILHookUndone;

            private void InvokeDetourEvent(Action<DetourInfo>? evt1, Action<DetourInfo>? evt2, SingleManagedDetourState node)
            {
                if (evt1 is not null || evt2 is not null)
                {
                    var info = Info.GetDetourInfo(node);
                    evt1?.Invoke(info);
                    evt2?.Invoke(info);
                }
            }

            private void InvokeILHookEvent(Action<ILHookInfo>? evt1, Action<ILHookInfo>? evt2, SingleILHookState entry)
            {
                if (evt1 is not null || evt2 is not null)
                {
                    var info = Info.GetILHookInfo(entry);
                    evt1?.Invoke(info);
                    evt2?.Invoke(info);
                }
            }
        }

        internal sealed class SingleManagedDetourState : SingleDetourStateBase
        {
            public readonly MethodInfo PublicTarget;
            public readonly MethodInfo InvokeTarget;
            public readonly IDetourTrampoline NextTrampoline;

            public DetourInfo? DetourInfo;

            public SingleManagedDetourState(IDetour dt) : base(dt)
            {
                PublicTarget = dt.PublicTarget;
                InvokeTarget = dt.InvokeTarget;
                NextTrampoline = dt.NextTrampoline;
            }
        }

        internal sealed class SingleILHookState : SingleDetourStateBase
        {
            public readonly ILContext.Manipulator Manip;
            public ILHookInfo? HookInfo;

            public SingleILHookState(IILHook hk) : base(hk)
            {
                Manip = hk.Manip;
            }
        }

        private static readonly ConcurrentDictionary<MethodBase, ManagedDetourState> detourStates = new();

        internal static ManagedDetourState GetDetourState(MethodBase method)
        {
            method = PlatformTriple.Current.GetIdentifiable(method);
            return detourStates.GetOrAdd(method, static m => new(m));
        }

        /// <summary>
        /// Gets the <see cref="MethodDetourInfo"/> for the provided method.
        /// </summary>
        /// <param name="method">The <see cref="MethodBase"/> to get a <see cref="MethodDetourInfo"/> for.</param>
        /// <returns>The <see cref="MethodDetourInfo"/> for <paramref name="method"/>.</returns>
        public static MethodDetourInfo GetDetourInfo(MethodBase method)
            => GetDetourState(method).Info;

        /// <summary>
        /// An event which is invoked whenever a detour is applied.
        /// </summary>
        /// <remarks>
        /// <see cref="Hook"/> is the only kind of detour, at present.
        /// </remarks>
        public static event Action<DetourInfo>? DetourApplied;
        /// <summary>
        /// An event which is invoked whenever a detour is undone.
        /// </summary>
        /// <remarks>
        /// <see cref="Hook"/> is the only kind of detour, at present.
        /// </remarks>
        public static event Action<DetourInfo>? DetourUndone;
        /// <summary>
        /// An event which is invoked whenever an <see cref="ILHook"/> is applied.
        /// </summary>
        public static event Action<ILHookInfo>? ILHookApplied;
        /// <summary>
        /// An event which is invoked whenever an <see cref="ILHook"/> is undone.
        /// </summary>
        public static event Action<ILHookInfo>? ILHookUndone;
    }
}
