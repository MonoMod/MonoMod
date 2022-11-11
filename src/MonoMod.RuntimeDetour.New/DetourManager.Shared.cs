using MonoMod.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    public static partial class DetourManager {

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
                            MMDbgLog.Warning($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        } else {
                            PrioInsert(node.BeforeThis, cur);
                            isAfter = true;
                        }
                    }
                    if (cur.Config.Before.Contains(node.Config.Id)) {
                        if (isBefore) {
                            MMDbgLog.Warning($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        } else {
                            PrioInsert(node.BeforeThis, cur);
                            isAfter = true;
                        }
                    }
                    if (cur.Config.After.Contains(node.Config.Id)) {
                        if (isAfter) {
                            MMDbgLog.Warning($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
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

        internal abstract class SingleDetourStateBase {
            public readonly IDetourFactory Factory;
            public readonly DetourConfig? Config;

            public object? ManagerData;

            public bool IsValid;
            public bool IsApplied => Volatile.Read(ref ManagerData) is not null;

            protected SingleDetourStateBase(IDetourBase detour) {
                Factory = detour.Factory;
                Config = detour.Config;
                ManagerData = null;
                IsValid = true;
            }
        }
    }
}
