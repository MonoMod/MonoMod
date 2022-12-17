using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MonoMod.RuntimeDetour {
    /// <summary>
    /// The entry point for introspection of active detours, and the type which manages their application.
    /// </summary>
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

        #region SyncInfo
        internal class DetourSyncInfo {
            public int ActiveCalls;
            public int UpdatingThread;

            public void WaitForChainUpdate() {
                _ = Interlocked.Decrement(ref ActiveCalls);

                if (UpdatingThread == EnvironmentEx.CurrentManagedThreadId) {
                    throw new InvalidOperationException("Method's detour chain is being updated by the current thread!");
                }

                var spin = new SpinWait();
                while (Volatile.Read(ref UpdatingThread) != -1) {
                    spin.SpinOnce();
                }
            }

            public void WaitForNoActiveCalls() {
                // TODO: find a decent way to prevent deadlocks here

                var spin = new SpinWait();
                while (Volatile.Read(ref ActiveCalls) > 0) {
                    spin.SpinOnce();
                }
            }
        }

        private static readonly FieldInfo DetourSyncInfo_ActiveCalls = typeof(DetourSyncInfo).GetField(nameof(DetourSyncInfo.ActiveCalls))!;
        private static readonly FieldInfo DetourSyncInfo_UpdatingThread = typeof(DetourSyncInfo).GetField(nameof(DetourSyncInfo.UpdatingThread))!;
        private static readonly MethodInfo DetourSyncInfo_WaitForChainUpdate = typeof(DetourSyncInfo).GetMethod(nameof(DetourSyncInfo.WaitForChainUpdate))!;

        private static readonly MethodInfo Interlocked_Increment
            = typeof(Interlocked).GetMethod(nameof(Interlocked.Increment), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int).MakeByRefType() }, null)!;
        private static readonly MethodInfo Interlocked_Decrement
            = typeof(Interlocked).GetMethod(nameof(Interlocked.Decrement), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int).MakeByRefType() }, null)!;


        private static MethodInfo GenerateSyncProxy(
            string innerName, MethodSignature Sig,
            Action<MethodDefinition, ILProcessor> emitLoadSyncInfo,
            Action<MethodDefinition, ILProcessor> emitInvoke
        ) {
            using var dmd = Sig.CreateDmd(DebugFormatter.Format($"SyncProxy<{innerName}>"));

            var il = dmd.GetILProcessor();
            var method = dmd.Definition;
            var module = dmd.Module!;

            var syncInfoTypeRef = module.ImportReference(typeof(DetourSyncInfo));
            var syncInfoVar = new VariableDefinition(syncInfoTypeRef);
            il.Body.Variables.Add(syncInfoVar);

            //scope = il.EmitNewTypedReference(SyncInfo, out _);
            emitLoadSyncInfo(method, il);
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
            il.Emit(OpCodes.Ldfld, module.ImportReference(DetourSyncInfo_UpdatingThread));
            il.Emit(OpCodes.Ldc_I4_M1);

            var noWait = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Beq_S, noWait);

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

            emitInvoke(method, il);
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
