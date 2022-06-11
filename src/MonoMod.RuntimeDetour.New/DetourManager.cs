using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.RuntimeDetour.Utils;
using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public static class DetourManager {
        #region Detour chain
        internal abstract class ChainNode {

            public ChainNode? Next;

            public abstract MethodBase Entry { get; }
            public abstract MethodBase NextTrampoline { get; }
            public abstract DetourConfig? Config { get; }
            public virtual bool DetourToFallback => true;

            private MethodBase? lastTarget;
            private ICoreDetour? trampolineDetour;

            public bool IsApplied { get; private set; }

            public virtual void UpdateDetour(IDetourFactory factory, MethodBase fallback) {
                var to = Next?.Entry;
                if (to is null && DetourToFallback) {
                    to = fallback;
                }

                if (to == lastTarget) {
                    // our target hasn't changed, don't need to update this link
                    return;
                }

                if (trampolineDetour is not null) {
                    trampolineDetour.Undo();
                    // TODO: cache trampolineDetours for a time, so they can be reused
                    trampolineDetour.Dispose();
                    trampolineDetour = null;
                }

                if (to is not null) {
                    trampolineDetour = factory.CreateDetour(NextTrampoline, to, applyByDefault: true);
                }

                lastTarget = to;
                IsApplied = true;
            }

            public void Remove() {
                if (trampolineDetour is not null) {
                    trampolineDetour.Undo();
                    // TODO: cache trampolineDetours for a time, so they can be reused
                    trampolineDetour.Dispose();
                    trampolineDetour = null;
                }
                lastTarget = null;
                Next = null;
                IsApplied = false;
            }
        }

        internal sealed class DetourChainNode : ChainNode {
            public DetourChainNode(IDetour detour) {
                Entry = detour.InvokeTarget;
                NextTrampoline = detour.NextTrampoline;
                Config = detour.Config;
                Factory = detour.Factory;
            }

            public override MethodBase Entry { get; }
            public override MethodBase NextTrampoline { get; }
            public override DetourConfig? Config { get; }
            public IDetourFactory Factory { get; }

            public DetourInfo? DetourInfo;
        }

        internal sealed class DetourSyncInfo {
            public int ActiveCalls;
            public bool UpdatingChain;

            public void WaitForChainUpdate() {
                _ = Interlocked.Decrement(ref ActiveCalls);
                var spin = new SpinWait();
                while (Volatile.Read(ref UpdatingChain)) {
                    spin.SpinOnce();
                }
            }

            public void WaitForNoActiveCalls() {
                var spin = new SpinWait();
                while (Volatile.Read(ref ActiveCalls) > 0) {
                    spin.SpinOnce();
                }
            }
        }

        // The root node is the existing method. It's NextTrampoline is the method, which is the same
        // as the entry point, because we want to detour the entry point. Entry should never be targeted though.
        internal sealed class RootChainNode : ChainNode {
            public override MethodBase Entry { get; }
            public override MethodBase NextTrampoline { get; }
            public override DetourConfig? Config => null;
            public override bool DetourToFallback => true; // we do want to detour to fallback, because our sync proxy might be waiting to call the method

            public readonly MethodSignature Sig;
            public readonly MethodBase SyncProxy;
            public readonly DetourSyncInfo SyncInfo = new();
            private readonly DataScope<DynamicReferenceManager.CellRef> syncProxyRefScope;

            public bool HasILHook;

            public RootChainNode(MethodBase method) {
                Sig = MethodSignature.ForMethod(method);
                Entry = method;
                NextTrampoline = TrampolinePool.Rent(Sig);
                SyncProxy = GenerateSyncProxy(out syncProxyRefScope);
            }

            private static readonly FieldInfo DetourSyncInfo_ActiveCalls = typeof(DetourSyncInfo).GetField(nameof(DetourSyncInfo.ActiveCalls))!;
            private static readonly FieldInfo DetourSyncInfo_UpdatingChain = typeof(DetourSyncInfo).GetField(nameof(DetourSyncInfo.UpdatingChain))!;
            private static readonly MethodInfo DetourSyncInfo_WaitForChainUpdate = typeof(DetourSyncInfo).GetMethod(nameof(DetourSyncInfo.WaitForChainUpdate))!;

            private static readonly MethodInfo Interlocked_Increment
                = typeof(Interlocked).GetMethod(nameof(Interlocked.Increment), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int).MakeByRefType() }, null)!;
            private static readonly MethodInfo Interlocked_Decrement
                = typeof(Interlocked).GetMethod(nameof(Interlocked.Decrement), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int).MakeByRefType() }, null)!;

            private MethodBase GenerateSyncProxy(out DataScope<DynamicReferenceManager.CellRef> scope) {
                using var dmd = Sig.CreateDmd($"SyncProxy<{Entry.GetID()}>");

                var method = dmd.Definition;
                var module = dmd.Module;
                var il = dmd.GetILProcessor();

                var syncInfoTypeRef = module.ImportReference(typeof(DetourSyncInfo));
                var syncInfoVar = new VariableDefinition(syncInfoTypeRef);
                il.Body.Variables.Add(syncInfoVar);

                scope = il.EmitNewTypedReference(SyncInfo, out _);
                il.Emit(OpCodes.Stloc, syncInfoVar);

                var checkWait = il.Create(OpCodes.Nop);
                il.Append(checkWait);

                // first increment ActiveCalls
                il.Emit(OpCodes.Ldloc, syncInfoVar);
                il.Emit(OpCodes.Ldflda, module.ImportReference(DetourSyncInfo_ActiveCalls));
                il.Emit(OpCodes.Call, module.ImportReference(Interlocked_Increment));
                il.Emit(OpCodes.Pop);

                // then check UpdatingChain
                il.Emit(OpCodes.Ldloc, syncInfoVar);
                il.Emit(OpCodes.Volatile);
                il.Emit(OpCodes.Ldfld, module.ImportReference(DetourSyncInfo_UpdatingChain));

                var noWait = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Brfalse_S, noWait);

                // if UpdatingChain was true, wait for that to finish, then jump back up to the top, because WaitForChainUpdate decrements
                il.Emit(OpCodes.Ldloc, syncInfoVar);
                il.Emit(OpCodes.Call, module.ImportReference(DetourSyncInfo_WaitForChainUpdate));
                il.Emit(OpCodes.Br_S, checkWait);

                // if UpdatingChain was false, we're good to continue
                il.Append(noWait);

                VariableDefinition? returnVar = null;
                if (Sig.ReturnType != typeof(void)) {
                    returnVar = new(method.ReturnType);
                    il.Body.Variables.Add(returnVar);
                }

                var eh = new ExceptionHandler(ExceptionHandlerType.Finally);
                il.Body.ExceptionHandlers.Add(eh);

                {
                    var i = il.Create(OpCodes.Nop);
                    il.Append(i);
                    eh.TryStart = i;
                }

                foreach (var p in method.Parameters) {
                    il.Emit(OpCodes.Ldarg, p);
                }
                il.Emit(OpCodes.Call, module.ImportReference(NextTrampoline));
                if (returnVar is not null) {
                    il.Emit(OpCodes.Stloc, returnVar);
                }

                var beforeReturnIns = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Leave_S, beforeReturnIns);

                var finallyStartIns = il.Create(OpCodes.Ldloc, syncInfoVar);
                eh.TryEnd = eh.HandlerStart = finallyStartIns;
                il.Append(finallyStartIns);
                il.Emit(OpCodes.Ldflda, module.ImportReference(DetourSyncInfo_ActiveCalls));
                il.Emit(OpCodes.Call, module.ImportReference(Interlocked_Decrement));
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Endfinally);
                eh.HandlerEnd = beforeReturnIns;

                il.Append(beforeReturnIns);
                if (returnVar is not null) {
                    il.Emit(OpCodes.Ldloc, returnVar);
                }
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }

            private ICoreDetour? syncDetour;

            public override void UpdateDetour(IDetourFactory factory, MethodBase fallback) {
                base.UpdateDetour(factory, fallback);

                syncDetour ??= factory.CreateDetour(Entry, SyncProxy, applyByDefault: false);

                if (!HasILHook && Next is null && syncDetour.IsApplied) {
                    syncDetour.Undo();
                } else if ((HasILHook || Next is not null) && !syncDetour.IsApplied) {
                    syncDetour.Apply();
                }
            }
        }
        #endregion

        #region ILHook chain
        private class ILHookEntry {
            public readonly IDetourFactory Factory;
            public readonly DetourConfig? Config;
            public readonly ILContext.Manipulator Manip;
            public ILContext? CurrentContext;
            public ILContext? LastContext;

            public ILHookEntry(IILHook hook) {
                Manip = hook.Manip;
                Config = hook.Config;
                Factory = hook.Factory;
            }

            public void Remove() {
                LastContext?.Dispose();
            }
        }
        #endregion

        #region DepGraph
        internal sealed class DepListNode<TNode> {
            public readonly DetourConfig Config;
            public readonly TNode ChainNode;

            public DepListNode<TNode>? Next;

            public DepListNode(DetourConfig config, TNode chainNode) {
                Config = config;
                ChainNode = chainNode;
            }
        }

        internal sealed class DepGraphNode<TNode> {
            public readonly DepListNode<TNode> ListNode;
            public DetourConfig Config => ListNode.Config;
            public readonly List<DepGraphNode<TNode>> BeforeThis = new();
            public bool Visiting;
            public bool Visited;

            public DepGraphNode(DepListNode<TNode> listNode) {
                ListNode = listNode;
            }
        }

        internal sealed class DepGraph<TNode> {
            private readonly List<DepGraphNode<TNode>> nodes = new();
            public DepListNode<TNode>? ListHead;

            private static void PrioInsert(List<DepGraphNode<TNode>> list, DepGraphNode<TNode> node) {
                if (node.Config.Priority is not { } nPrio) {
                    list.Add(node);
                    return;
                }

                var insertIdx = -1;
                for (var i = 0; i < list.Count; i++) {
                    var cur = list[i];
                    if (cur.Config.Priority is { } cPrio) {
                        if (nPrio > cPrio) {
                            insertIdx = i;
                            break;
                        }
                    } else {
                        // if we've hit the block of no priorities, then we have our location
                        insertIdx = i;
                        break;
                    }
                }

                if (insertIdx < 0) {
                    insertIdx = list.Count;
                }
                list.Insert(insertIdx, node);
            }

            public void Insert(DepGraphNode<TNode> node) {
                node.ListNode.Next = null;
                node.BeforeThis.Clear();
                node.Visited = false;
                node.Visiting = false;

                var insertIdx = -1;
                for (var i = 0; i < nodes.Count; i++) {
                    var cur = nodes[i];
                    cur.Visited = false;

                    if (insertIdx < 0 && node.Config.Priority is { } nPrio) {
                        // if the current node is the first node with lower priority, insert here
                        if (cur.Config.Priority is { } cPrio) {
                            if (nPrio > cPrio) {
                                insertIdx = i;
                            }
                        } else {
                            // if we've hit the block of no priorities, then we have our location
                            insertIdx = i;
                        }
                    }

                    bool isBefore = false,
                        isAfter = false;
                    if (node.Config.Before.Contains(cur.Config.Id)) {
                        PrioInsert(cur.BeforeThis, node);
                        isBefore = true;
                    }
                    if (node.Config.After.Contains(cur.Config.Id)) {
                        if (isBefore) {
                            MMDbgLog.Log($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        } else {
                            PrioInsert(node.BeforeThis, cur);
                            isAfter = true;
                        }
                    }
                    if (cur.Config.Before.Contains(node.Config.Id)) {
                        if (isBefore) {
                            MMDbgLog.Log($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        } else {
                            PrioInsert(node.BeforeThis, cur);
                            isAfter = true;
                        }
                    }
                    if (cur.Config.After.Contains(node.Config.Id)) {
                        if (isAfter) {
                            MMDbgLog.Log($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        } else {
                            PrioInsert(cur.BeforeThis, node);
                            //isBefore = true;
                        }
                    }
                }

                if (insertIdx < 0) {
                    insertIdx = nodes.Count;
                }
                nodes.Insert(insertIdx, node);

                UpdateList();
            }

            public void Remove(DepGraphNode<TNode> node) {
                nodes.Remove(node);
                foreach (var cur in nodes) {
                    cur.BeforeThis.Remove(node);
                    cur.Visited = false;
                }
                node.BeforeThis.Clear();
                node.Visited = false;
                node.Visiting = false;
                node.ListNode.Next = null;

                UpdateList();
            }

            private readonly DepListNode<TNode> dummyListNode = new(null!, default!);

            private void UpdateList() {
                var dummy = dummyListNode;
                dummy.Next = null;

                var nextHolder = dummy;
                foreach (var node in nodes) {
                    InsertListNode(ref nextHolder, node);
                }
                ListHead = dummy.Next;
            }

            private void InsertListNode(ref DepListNode<TNode> nextHolder, DepGraphNode<TNode> node) {
                if (node.Visiting) {
                    throw new InvalidOperationException("Cycle detected");
                }
                if (node.Visited) {
                    return;
                }

                node.Visiting = true;
                try {
                    var listNode = node.ListNode;
                    listNode.Next = null;

                    foreach (var before in node.BeforeThis) {
                        InsertListNode(ref nextHolder, before);
                    }

                    nextHolder.Next = listNode;
                    nextHolder = listNode;
                    node.Visited = true;
                } finally {
                    node.Visiting = false;
                }
            }
        }
        #endregion

        internal sealed class DetourState {
            public readonly MethodBase Source;
            public readonly MethodBase ILCopy;
            public MethodBase EndOfChain;


            public DetourState(MethodBase src) {
                Source = src;
                ILCopy = src.CreateILCopy();
                EndOfChain = ILCopy;
                detourList = new(src);
            }

            private MethodDetourInfo? info;
            public MethodDetourInfo Info => info ??= new(this);

            private readonly DepGraph<ChainNode> detourGraph = new();
            internal readonly RootChainNode detourList;
            private ChainNode? noConfigChain;

            internal SpinLock detourLock = new(true);

            public void AddDetour(IDetour detour) {
                var lockTaken = false;
                try {
                    detourLock.Enter(ref lockTaken);
                    if (detour.ManagerData is not null)
                        throw new InvalidOperationException("Trying to add a detour which was already added");

                    var cnode = new DetourChainNode(detour);
                    if (cnode.Config is { } cfg) {
                        var listNode = new DepListNode<ChainNode>(cfg, cnode);
                        var graphNode = new DepGraphNode<ChainNode>(listNode);

                        detourGraph.Insert(graphNode);

                        detour.ManagerData = graphNode;
                    } else {
                        cnode.Next = noConfigChain;
                        noConfigChain = cnode;

                        detour.ManagerData = cnode;
                    }

                    UpdateChain(detour.Factory);
                } finally {
                    if (lockTaken)
                        detourLock.Exit(true);
                }
            }

            public void RemoveDetour(IDetour detour) {
                var lockTaken = false;
                try {
                    detourLock.Enter(ref lockTaken);
                    switch (detour.ManagerData) {
                        case null:
                            throw new InvalidOperationException("Trying to remove detour which wasn't added");

                        case DepGraphNode<ChainNode> gn:
                            RemoveGraphDetour(detour, gn);
                            break;

                        case DetourChainNode cn:
                            RemoveNoConfigDetour(detour, cn);
                            break;

                        default:
                            throw new InvalidOperationException("Trying to remove detour with unknown manager data");
                    }
                } finally {
                    if (lockTaken)
                        detourLock.Exit(true);
                }
            }

            private void RemoveGraphDetour(IDetour detour, DepGraphNode<ChainNode> node) {
                detourGraph.Remove(node);
                UpdateChain(detour.Factory);
                node.ListNode.ChainNode.Remove();
            }

            private void RemoveNoConfigDetour(IDetour detour, DetourChainNode node) {
                ref var chain = ref noConfigChain;
                while (chain is not null) {
                    if (ReferenceEquals(chain, node)) {
                        chain = node.Next;
                        node.Next = null;
                        break;
                    }

                    chain = ref chain.Next;
                }

                UpdateChain(detour.Factory);
                node.Remove();
            }

            private readonly DepGraph<ILHookEntry> ilhookGraph = new();
            private readonly List<ILHookEntry> noConfigIlhooks = new();

            public void AddILHook(IILHook ilhook) {
                var lockTaken = false;
                try {
                    detourLock.Enter(ref lockTaken);
                    if (ilhook.ManagerData is not null)
                        throw new InvalidOperationException("Trying to add an IL hook which was already added");

                    var entry = new ILHookEntry(ilhook);
                    if (entry.Config is { } cfg) {
                        var listNode = new DepListNode<ILHookEntry>(cfg, entry);
                        var graphNode = new DepGraphNode<ILHookEntry>(listNode);

                        ilhookGraph.Insert(graphNode);

                        ilhook.ManagerData = graphNode;
                    } else {
                        noConfigIlhooks.Add(entry);
                        ilhook.ManagerData = entry;
                    }

                    UpdateEndOfChain();
                    UpdateChain(ilhook.Factory);
                } finally {
                    if (lockTaken)
                        detourLock.Exit(true);
                }
            }

            public void RemoveILHook(IILHook ilhook) {
                var lockTaken = false;
                try {
                    detourLock.Enter(ref lockTaken);
                    switch (ilhook.ManagerData) {
                        case null:
                            throw new InvalidOperationException("Trying to remove IL hook which wasn't added");

                        case DepGraphNode<ILHookEntry> gn:
                            RemoveGraphILHook(ilhook, gn);
                            break;

                        case ILHookEntry cn:
                            RemoveNoConfigILHook(ilhook, cn);
                            break;

                        default:
                            throw new InvalidOperationException("Trying to remove IL hook with unknown manager data");
                    }
                } finally {
                    if (lockTaken)
                        detourLock.Exit(true);
                }
            }

            private void RemoveGraphILHook(IILHook ilhook, DepGraphNode<ILHookEntry> node) {
                ilhookGraph.Remove(node);
                UpdateEndOfChain();
                UpdateChain(ilhook.Factory);
                CleanILContexts();
                node.ListNode.ChainNode.Remove();
            }

            private void RemoveNoConfigILHook(IILHook ilhook, ILHookEntry node) {
                noConfigIlhooks.Remove(node);
                UpdateEndOfChain();
                UpdateChain(ilhook.Factory);
                CleanILContexts();
                node.Remove();
            }

            private void UpdateEndOfChain() {
                if (noConfigIlhooks.Count == 0 && ilhookGraph.ListHead is null) {
                    detourList.HasILHook = false;
                    EndOfChain = ILCopy;
                    return;
                }

                detourList.HasILHook = true;

                using var dmd = new DynamicMethodDefinition(Source);

                var def = dmd.Definition;
                var cur = ilhookGraph.ListHead;
                while (cur is not null) {
                    InvokeManipulator(cur.ChainNode, def);
                    cur = cur.Next;
                }

                foreach (var node in noConfigIlhooks) {
                    InvokeManipulator(node, def);
                }

                EndOfChain = dmd.Generate();
            }

            private static void InvokeManipulator(ILHookEntry entry, MethodDefinition def) {
                //entry.LastContext?.Dispose(); // we can't safely clean up the old context until after we've updated the chain to point at the new method
                var il = new ILContext(def);
                entry.CurrentContext = il;
                il.ReferenceBag = RuntimeILReferenceBag.Instance;
                il.Invoke(entry.Manip);
                if (il.IsReadOnly) {
                    il.Dispose();
                    return;
                }

                // Free the now useless MethodDefinition and ILProcessor references.
                // This also prevents clueless people from storing the ILContext elsewhere
                // and reusing it outside of the IL manipulation context.
                il.MakeReadOnly();
                return;
            }

            private void CleanILContexts() {
                var cur = ilhookGraph.ListHead;
                while (cur is not null) {
                    CleanContext(cur.ChainNode);
                    cur = cur.Next;
                }

                foreach (var node in noConfigIlhooks) {
                    CleanContext(node);
                }

                static void CleanContext(ILHookEntry entry) {
                    if (entry.CurrentContext == entry.LastContext)
                        return;
                    var old = entry.LastContext;
                    entry.LastContext = entry.CurrentContext;
                    old?.Dispose();
                }
            }

            private void UpdateChain(IDetourFactory updatingFactory) {
                var graphNode = detourGraph.ListHead;

                ChainNode? chain = null;
                ref var next = ref chain;
                while (graphNode is not null) {
                    next = graphNode.ChainNode;
                    next = ref next.Next;
                    next = null; // clear it to be safe before continuing
                    graphNode = graphNode.Next;
                }

                // after building the chain from the graph list, add the noConfigChain
                next = noConfigChain;

                // our chain is now fully built, with the head in chain
                detourList.Next = chain; // detourList is the head of the real chain, and represents the original method

                Volatile.Write(ref detourList.SyncInfo.UpdatingChain, true);
                detourList.SyncInfo.WaitForNoActiveCalls();
                try {
                    chain = detourList;
                    while (chain is not null) {
                        // we want to use the factory for the next node first
                        var fac = (chain.Next as DetourChainNode)?.Factory;
                        // then, if that doesn't exist, the current factory
                        fac ??= (chain as DetourChainNode)?.Factory;
                        // and if that doesn't exist, then the updating factory
                        fac ??= updatingFactory;
                        chain.UpdateDetour(fac, EndOfChain);

                        chain = chain.Next;
                    }
                } finally {
                    Volatile.Write(ref detourList.SyncInfo.UpdatingChain, false);
                }
            }
        }

        private static readonly ConcurrentDictionary<MethodBase, DetourState> detourStates = new();

        internal static DetourState GetDetourState(MethodBase method)
            => detourStates.GetOrAdd(method, m => new(m));

        public static MethodDetourInfo GetDetourInfo(MethodBase method)
            => GetDetourState(method).Info;
    }

    public sealed class MethodDetourInfo {
        internal readonly DetourManager.DetourState state;
        internal MethodDetourInfo(DetourManager.DetourState state) {
            this.state = state;
        }

        public MethodBase Method => state.Source;

        public bool HasActiveCall => Volatile.Read(ref state.detourList.SyncInfo.ActiveCalls) > 0;

        private DetourCollection? lazyDetours;
        public DetourCollection Detours => lazyDetours ??= new(this);

        public DetourInfo? FirstDetour
            => state.detourList.Next is DetourManager.DetourChainNode cn ? GetDetourInfo(cn) : null;

        internal DetourInfo GetDetourInfo(DetourManager.DetourChainNode node) {
            var existingInfo = node.DetourInfo;
            if (existingInfo is null || existingInfo.MethodInfo != this) {
                return node.DetourInfo = new(node, this);
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
                mdi.EnterLock(ref lockTaken);
            }

            public void Dispose() {
                if (lockTaken)
                    mdi.ExitLock();
            }
        }
    }

    public sealed class DetourCollection : IEnumerable<DetourInfo> {
        private readonly MethodDetourInfo mdi;
        internal DetourCollection(MethodDetourInfo mdi)
            => this.mdi = mdi;

        public Enumerator GetEnumerator() => new(mdi);

        IEnumerator<DetourInfo> IEnumerable<DetourInfo>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // TODO: track and check version to keep note of when the chain changes
        public struct Enumerator : IEnumerator<DetourInfo> {
            private readonly MethodDetourInfo mdi;
            private DetourManager.ChainNode? curNode;

            internal Enumerator(MethodDetourInfo mdi) {
                this.mdi = mdi;
                curNode = mdi.state.detourList;
            }

            public DetourInfo Current => mdi.GetDetourInfo((DetourManager.DetourChainNode) curNode!);

            object IEnumerator.Current => Current;

            [MemberNotNullWhen(true, nameof(curNode))]
            public bool MoveNext() {
                curNode = curNode?.Next;
                return curNode is not null;
            }

            public void Reset() {
                curNode = mdi.state.detourList;
            }

            public void Dispose() { }
        }
    }

    public sealed class DetourInfo {
        private readonly DetourManager.DetourChainNode detour;

        internal DetourInfo(DetourManager.DetourChainNode detour, MethodDetourInfo methodInfo) {
            this.detour = detour;
            MethodInfo = methodInfo;
        }

        public MethodDetourInfo MethodInfo { get; }

        public bool IsApplied => detour.IsApplied;
        public DetourConfig? Config => detour.Config;

        public DetourInfo? Next
            => detour.Next is DetourManager.DetourChainNode cn ? MethodInfo.GetDetourInfo(cn) : null;
    }
}
