using MonoMod.Core;
using MonoMod.Core.Utils;
using MonoMod.RuntimeDetour.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    internal static class DetourManager {

        private abstract class ChainNode {

            public ChainNode? Next;

            public abstract MethodBase Entry { get; }
            public abstract MethodBase NextTrampoline { get; }
            public abstract DetourConfig? Config { get; }
            public virtual bool DetourToFallback => true;

            private MethodBase? lastTarget;
            private ICoreDetour? trampolineDetour;

            public void UpdateDetour(IDetourFactory factory, MethodBase fallback) {
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
            }
        }

        private sealed class DetourChainNode : ChainNode {
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
        }

        // The root node is the existing method. It's NextTrampoline is the method, which is the same
        // as the entry point, because we want to detour the entry point. Entry should never be targeted though.
        private sealed class RootChainNode : ChainNode {
            public override MethodBase Entry => NextTrampoline;
            public override MethodBase NextTrampoline { get; }
            public override DetourConfig? Config => null;
            public override bool DetourToFallback => false;

            public RootChainNode(MethodBase method) {
                NextTrampoline = method;
            }
        }

        private sealed class DepListNode {
            public readonly DetourConfig Config;
            public readonly DetourChainNode ChainNode;

            public DepListNode? Next;

            public DepListNode(DetourConfig config, DetourChainNode chainNode) {
                Config = config;
                ChainNode = chainNode;
            }
        }

        private sealed class DepGraphNode {
            public readonly DepListNode ListNode;
            public DetourConfig Config => ListNode.Config;
            public readonly List<DepGraphNode> BeforeThis = new();
            public bool Visiting;
            public bool Visited;

            public DepGraphNode(DepListNode listNode) {
                ListNode = listNode;
            }
        }

        private sealed class DepGraph {
            private readonly List<DepGraphNode> nodes = new();
            public DepListNode? ListHead;

            private static void PrioInsert(List<DepGraphNode> list, DepGraphNode node) {
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

            public void Insert(DepGraphNode node) {
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
                            isBefore = true;
                        }
                    }
                }

                if (insertIdx < 0) {
                    insertIdx = nodes.Count;
                }
                nodes.Insert(insertIdx, node);

                UpdateList();
            }

            public void Remove(DepGraphNode node) {
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

            private readonly DepListNode dummyListNode = new(null!, null!);

            private void UpdateList() {
                var dummy = dummyListNode;
                dummy.Next = null;

                var nextHolder = dummy;
                foreach (var node in nodes) {
                    InsertListNode(ref nextHolder, node);
                }
                ListHead = dummy.Next;
            }

            private void InsertListNode(ref DepListNode nextHolder, DepGraphNode node) {
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

        internal class DetourState {
            public readonly MethodBase Source;
            public readonly MethodBase ILCopy;

            private readonly object sync = new();

            public DetourState(MethodBase src) {
                Source = src;
                ILCopy = src.CreateILCopy();
                detourList = new(src);
            }

            private readonly DepGraph graph = new();
            private readonly RootChainNode detourList;
            private ChainNode? noConfigChain;

            public void AddDetour(IDetour detour) {
                lock (sync) {
                    if (detour.ManagerData is not null)
                        throw new InvalidOperationException("Trying to add a detour which was already added");

                    var cnode = new DetourChainNode(detour);
                    if (cnode.Config is { } cfg) {
                        var listNode = new DepListNode(cfg, cnode);
                        var graphNode = new DepGraphNode(listNode);

                        graph.Insert(graphNode);

                        detour.ManagerData = graphNode;
                    } else {
                        cnode.Next = noConfigChain;
                        noConfigChain = cnode;

                        detour.ManagerData = cnode;
                    }

                    UpdateChain(detour.Factory);
                }
            }

            public void RemoveDetour(IDetour detour) {
                lock (sync) {
                    switch (detour.ManagerData) {
                        case null:
                            throw new InvalidOperationException("Trying to remove detour which wasn't added");

                        case DepGraphNode gn:
                            RemoveGraphDetour(detour, gn);
                            break;

                        case DetourChainNode cn:
                            RemoveNoConfigDetour(detour, cn);
                            break;

                        default:
                            throw new InvalidOperationException("Trying to remove detour with unknown manager data");
                    }
                }
            }

            private void RemoveGraphDetour(IDetour detour, DepGraphNode node) {
                graph.Remove(node);
                UpdateChain(detour.Factory);
                node.ListNode.ChainNode.Remove();
            }

            private void RemoveNoConfigDetour(IDetour detour, DetourChainNode node) {
                var chain = noConfigChain;
                while (chain is not null) {
                    if (ReferenceEquals(chain.Next, node)) {
                        chain.Next = node.Next;
                        node.Next = null;
                        break;
                    }

                    chain = chain.Next;
                }

                UpdateChain(detour.Factory);
                node.Remove();
            }

            private void UpdateChain(IDetourFactory updatingFactory) {
                var graphNode = graph.ListHead;

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

                chain = detourList;
                while (chain is not null) {
                    // we want to use the factory for the next node first
                    var fac = (chain.Next as DetourChainNode)?.Factory;
                    // then, if that doesn't exist, the current factory
                    fac ??= (chain as DetourChainNode)?.Factory;
                    // and if that doesn't exist, then the updating factory
                    fac ??= updatingFactory;
                    chain.UpdateDetour(fac, ILCopy);

                    chain = chain.Next;
                }
            }
        }

        private static ConcurrentDictionary<MethodBase, DetourState> detourStates = new();

        public static DetourState GetDetourState(MethodBase method)
            => detourStates.GetOrAdd(method, m => new(m));
    }
}
