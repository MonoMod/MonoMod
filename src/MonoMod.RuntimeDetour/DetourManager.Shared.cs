using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// The entry point for introspection of active detours, and the type which manages their application.
    /// </summary>
    public static partial class DetourManager
    {

        #region DepGraph
        internal sealed class DepListNode<TNode>
        {
            public readonly DetourConfig Config;
            public readonly TNode ChainNode;

            public DepListNode<TNode>? Next;

            public DepListNode(DetourConfig config, TNode chainNode)
            {
                Config = config;
                ChainNode = chainNode;
            }
        }

        internal sealed class DepGraphNode<TNode>
        {
            public readonly DepListNode<TNode> ListNode;
            public DetourConfig Config => ListNode.Config;
            public readonly List<DepGraphNode<TNode>> BeforeThis = new();
            public bool Visiting;
            public bool Visited;

            public DepGraphNode(DepListNode<TNode> listNode)
            {
                ListNode = listNode;
            }
        }

        internal sealed class DepGraph<TNode>
        {
            private readonly List<DepGraphNode<TNode>> nodes = new();
            public DepListNode<TNode>? ListHead;

            private static void PrioInsert(List<DepGraphNode<TNode>> list, DepGraphNode<TNode> node)
            {
                if (node.Config.Priority is not { } nPrio)
                {
                    list.Add(node);
                    return;
                }

                var insertIdx = -1;
                for (var i = 0; i < list.Count; i++)
                {
                    var cur = list[i];
                    if (cur.Config.Priority is { } cPrio)
                    {
                        // if the current node is the first node with lower or equal priority, insert here
                        if (nPrio >= cPrio)
                        {
                            insertIdx = i;
                            break;
                        }
                    }
                    else
                    {
                        // if we've hit the block of no priorities, then we have our location
                        insertIdx = i;
                        break;
                    }
                }

                if (insertIdx < 0)
                {
                    insertIdx = list.Count;
                }
                else
                {
                    // ensure that nodes with higher sub-priority come first
                    for (; insertIdx < list.Count; insertIdx++)
                    {
                        var cur = list[insertIdx];
                        if (cur.Config.Priority != node.Config.Priority || cur.Config.SubPriority <= node.Config.SubPriority)
                            break;
                    }
                }
                list.Insert(insertIdx, node);
            }

            public void Insert(DepGraphNode<TNode> node)
            {
                node.ListNode.Next = null;
                node.BeforeThis.Clear();
                node.Visited = false;
                node.Visiting = false;

                var insertIdx = -1;
                for (var i = 0; i < nodes.Count; i++)
                {
                    var cur = nodes[i];
                    cur.Visited = false;

                    if (insertIdx < 0 && node.Config.Priority is { } nPrio)
                    {
                        // if the current node is the first node with lower or equal priority, insert here
                        if (cur.Config.Priority is { } cPrio)
                        {
                            if (nPrio >= cPrio)
                            {
                                insertIdx = i;
                            }
                        }
                        else
                        {
                            // if we've hit the block of no priorities, then we have our location
                            insertIdx = i;
                        }
                    }

                    bool isBefore = false,
                        isAfter = false;
                    if (node.Config.Before.Contains(cur.Config.Id))
                    {
                        PrioInsert(cur.BeforeThis, node);
                        isBefore = true;
                    }
                    if (node.Config.After.Contains(cur.Config.Id))
                    {
                        if (isBefore)
                        {
                            MMDbgLog.Warning($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        }
                        else
                        {
                            PrioInsert(node.BeforeThis, cur);
                            isAfter = true;
                        }
                    }
                    if (cur.Config.Before.Contains(node.Config.Id))
                    {
                        if (isBefore)
                        {
                            MMDbgLog.Warning($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        }
                        else
                        {
                            PrioInsert(node.BeforeThis, cur);
                            isAfter = true;
                        }
                    }
                    if (cur.Config.After.Contains(node.Config.Id))
                    {
                        if (isAfter)
                        {
                            MMDbgLog.Warning($"Detour '{node.Config.Id}' is marked as being both before and after '{cur.Config.Id}'");
                        }
                        else
                        {
                            PrioInsert(cur.BeforeThis, node);
                            //isBefore = true;
                        }
                    }
                }

                if (insertIdx < 0)
                {
                    insertIdx = nodes.Count;
                }
                else
                {
                    // ensure that nodes with higher sub-priority come first
                    for (; insertIdx < nodes.Count; insertIdx++)
                    {
                        var cur = nodes[insertIdx];
                        if (cur.Config.Priority != node.Config.Priority || cur.Config.SubPriority <= node.Config.SubPriority)
                            break;
                    }
                }
                nodes.Insert(insertIdx, node);

                UpdateList();
            }

            public void Remove(DepGraphNode<TNode> node)
            {
                nodes.Remove(node);
                foreach (var cur in nodes)
                {
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

            private void UpdateList()
            {
                var dummy = dummyListNode;
                dummy.Next = null;

                var nextHolder = dummy;
                foreach (var node in nodes)
                {
                    InsertListNode(ref nextHolder, node);
                }
                ListHead = dummy.Next;
            }

            private static void InsertListNode(ref DepListNode<TNode> nextHolder, DepGraphNode<TNode> node)
            {
                if (node.Visiting)
                {
                    throw new InvalidOperationException("Cycle detected");
                }
                if (node.Visited)
                {
                    return;
                }

                node.Visiting = true;
                try
                {
                    var listNode = node.ListNode;
                    listNode.Next = null;

                    foreach (var before in node.BeforeThis)
                    {
                        InsertListNode(ref nextHolder, before);
                    }

                    nextHolder.Next = listNode;
                    nextHolder = listNode;
                    node.Visited = true;
                }
                finally
                {
                    node.Visiting = false;
                }
            }
        }
        #endregion

        #region SyncInfo
        internal class DetourSyncInfo
        {
            public MethodBase? SyncProxy;
            public int ActiveCalls;
            public int UpdatingThread;

            public bool WaitForChainUpdate()
            {
                // If this is a nested call, allow the call to continue without waiting for the update to avoid deadlocks
                if (Volatile.Read(ref ActiveCalls) > 1 && DetermineThreadCallDepth() > 1)
                    return true;

                _ = Interlocked.Decrement(ref ActiveCalls);

                if (UpdatingThread == EnvironmentEx.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException("Method's detour chain is being updated by the current thread!");
                }

                var spin = new SpinWait();
                while (Volatile.Read(ref UpdatingThread) != -1)
                {
                    spin.SpinOnce();
                }

                return false;
            }

            public void WaitForNoActiveCalls(out bool hasActiveCallsFromThread)
            {
                var threadCallDepth = DetermineThreadCallDepth();
                hasActiveCallsFromThread = threadCallDepth > 0;

                // Wait for other threads to have returned from the function
                var spin = new SpinWait();
                while (Volatile.Read(ref ActiveCalls) > threadCallDepth)
                {
                    spin.SpinOnce();
                }
            }

            private int DetermineThreadCallDepth()
            {
                if (Volatile.Read(ref ActiveCalls) <= 0 || SyncProxy == null)
                    return 0;

                // Determine active call depth of the current thread
                var stackFrames = new StackTrace().GetFrames();
                var syncProxyIdentif = PlatformTriple.Current.GetIdentifiable(SyncProxy);
                return stackFrames.Count(f => f.GetMethod() is { } m && PlatformTriple.Current.GetIdentifiable(m) == syncProxyIdentif);
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
            Action<MethodDefinition, ILProcessor, Action> emitInvoke,
            Action<MethodDefinition, ILProcessor, Action>? emitLastCallReturn = null
        )
        {
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
            // if WaitForChainUpdate returns true, continue with the call without waiting anyway
            il.Emit(OpCodes.Ldloc, syncInfoVar);
            il.Emit(OpCodes.Call, module.ImportReference(DetourSyncInfo_WaitForChainUpdate));
            il.Emit(OpCodes.Brtrue_S, noWait);
            il.Emit(OpCodes.Br_S, checkWait);

            // if UpdatingChain was false, we're good to continue
            il.Append(noWait);

            VariableDefinition? returnVar = null;
            if (Sig.ReturnType != typeof(void))
            {
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

            emitInvoke(method, il, () => il.Emit(OpCodes.Ldloc, syncInfoVar));
            if (returnVar is not null)
            {
                il.Emit(OpCodes.Stloc, returnVar);
            }

            var beforeReturnIns = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Leave_S, beforeReturnIns);

            var finallyStartIns = il.Create(OpCodes.Ldloc, syncInfoVar);
            eh.TryEnd = eh.HandlerStart = finallyStartIns;
            il.Append(finallyStartIns);
            il.Emit(OpCodes.Ldflda, module.ImportReference(DetourSyncInfo_ActiveCalls));
            il.Emit(OpCodes.Call, module.ImportReference(Interlocked_Decrement));

            if (emitLastCallReturn == null)
            {
                il.Emit(OpCodes.Pop);
            }
            else
            {
                // if Interlocked.Decrement returned zero, this has been the last call to the method
                var notLastCall = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Brtrue_S, notLastCall);
                emitLastCallReturn(method, il, () => il.Emit(OpCodes.Ldloc, syncInfoVar));
                il.Append(notLastCall);
            }

            il.Emit(OpCodes.Endfinally);
            eh.HandlerEnd = beforeReturnIns;

            il.Append(beforeReturnIns);
            if (returnVar is not null)
            {
                il.Emit(OpCodes.Ldloc, returnVar);
            }
            il.Emit(OpCodes.Ret);

            return dmd.Generate();
        }
        #endregion

        internal abstract class SingleDetourStateBase
        {
            public readonly IDetourFactory Factory;
            public readonly DetourConfig? Config;

            public object? ManagerData;

            public bool IsValid;
            public bool IsApplied => Volatile.Read(ref ManagerData) is not null;

            protected SingleDetourStateBase(IDetourBase detour)
            {
                Factory = detour.Factory;
                Config = detour.Config;
                ManagerData = null;
                IsValid = true;
            }
        }

        private static MethodInfo GenerateRemovedStub(MethodSignature trampolineSig)
        {
            using var dmd = trampolineSig.CreateDmd(DebugFormatter.Format($"RemovedStub<{trampolineSig}>"));
            Helpers.Assert(dmd.Module is not null && dmd.Definition is not null);
            var module = dmd.Module;

            var il = dmd.GetILProcessor();

            // instantiate a new System.InvalidOperationException and throw it
            il.Emit(OpCodes.Ldstr, "Detour has been removed");
            il.Emit(OpCodes.Newobj, module.ImportReference(typeof(InvalidOperationException).GetConstructor(new Type[] { typeof(string) })));
            il.Emit(OpCodes.Throw);

            return dmd.Generate();
        }

        private static readonly ConditionalWeakTable<MethodSignature, MethodInfo> removedStubCache = new();
        private static MethodInfo GetRemovedStub(MethodSignature trampolineSig)
        {
            return removedStubCache.GetValue(trampolineSig, orig => GenerateRemovedStub(trampolineSig));
        }
    }
}
