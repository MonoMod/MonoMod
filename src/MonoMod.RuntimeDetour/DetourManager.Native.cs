using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonoMod.RuntimeDetour
{
    public static partial class DetourManager
    {
        #region Detour Chain
        internal abstract class NativeChainNode
        {

            public NativeChainNode? Next;

            public abstract Delegate EntryDelegate { get; }

            public abstract void UpdateChain(IDetourFactory factory, Delegate? fallback);
            public virtual void Remove() { }

        }

        internal sealed class NativeDetourChainNode : NativeChainNode
        {
            public NativeDetourChainNode(SingleNativeDetourState detour)
            {
                Detour = detour;
                if (detour.HasOrigParam)
                {
                    ChainState = new(detour.Invoker, detour.NativeDelegateType);
                }
            }

            public readonly SingleNativeDetourState Detour;
            public DetourConfig? Config => Detour.Config;

            public readonly ChainDelegateState? ChainState;

            public override Delegate EntryDelegate => ChainState?.GetDelegate() ?? Detour.Invoker;

            public override void UpdateChain(IDetourFactory factory, Delegate? fallback)
            {
                var del = Next?.EntryDelegate ?? fallback;
                del = del?.CastDelegate(Detour.NativeDelegateType);
                if (ChainState is not null)
                {
                    ChainState.Next = del;
                }
            }

            public override void Remove()
            {
                if (ChainState is not null)
                {
                    ChainState.Remove();
                }
            }
        }

        internal sealed class NativeDetourSyncInfo : DetourSyncInfo
        {
            public Delegate? FirstDelegate;
        }

        private static readonly FieldInfo NativeDetourSyncInfo_FirstDelegate = typeof(NativeDetourSyncInfo).GetField(nameof(NativeDetourSyncInfo.FirstDelegate))!;

        internal sealed class RootNativeDetourChainNode : NativeChainNode
        {
            public readonly NativeDetourSyncInfo SyncInfo;
            public Type EntryType = null!;
            public Delegate SyncProxyDelegate = null!;
            public IntPtr SyncProxyNativeEntry;

            public readonly IntPtr Function;

            public RootNativeDetourChainNode(IntPtr function)
            {
                SyncInfo = new();
                Function = function;
            }

            public void MaybeSetEntryType(Type type)
            {
                if (EntryType is not null)
                    return;

                var delInvoke = type.GetMethod("Invoke")!;
                var syncMeth = GenerateSyncProxy(DebugFormatter.Format($"native->managed {type}"), MethodSignature.ForMethod(delInvoke, ignoreThis: true),
                    (method, il) =>
                    { // load the sync info
                        // add a first parameter of type NativeDetourSyncInfo
                        method.Parameters.Insert(0, new(method.Module.ImportReference(typeof(NativeDetourSyncInfo))));
                        // and load that first param
                        il.Emit(OpCodes.Ldarg_0);
                    },
                    (method, il, loadSyncInfo) =>
                    { // emit the call
                        il.Emit(OpCodes.Ldarg_0); // load our sync info
                        // now we load the FirstDelegate field, and invoke that
                        il.Emit(OpCodes.Ldfld, method.Module.ImportReference(NativeDetourSyncInfo_FirstDelegate));
                        // now we load all of the (remaining) arguments
                        for (var i = 1; i < method.Parameters.Count; i++)
                        {
                            il.Emit(OpCodes.Ldarg, i);
                        }
                        // and callvirt the delegate's invoke method
                        il.Emit(OpCodes.Callvirt, method.Module.ImportReference(delInvoke));
                    }
                );

                // now we want to use syncMeth to generate our delegate
                var del = syncMeth.CreateDelegate(type, SyncInfo);
                // and go ahead and proactively generate the native entry point
                SyncProxyNativeEntry = Marshal.GetFunctionPointerForDelegate(del);
                // note: we defer writing to any fields until *after* GetFunctionPointerForDelegate because we want it to throw if no marshaling info is set up
                // if it does, we want a later caller to be able to set the entry type here
                EntryType = type;
                SyncInfo.SyncProxy = syncMeth;
                SyncProxyDelegate = del;
            }

            public override Delegate EntryDelegate => throw new InvalidOperationException(); // don't need it for the root node, as it is what calls into all later nodes

            private Delegate? origDelegate;
            public Delegate? OrigDelegate
            {
                get
                {
                    if (origDelegate is { } del)
                        return del;
                    if (nativeDetour is not { } detour)
                        return null;
                    if (!detour.HasOrigEntrypoint)
                        return null;
                    return origDelegate = Marshal.GetDelegateForFunctionPointer(detour.OrigEntrypoint, EntryType);
                }
            }

            private ICoreNativeDetour? nativeDetour;
            private IntPtr lastNativeEntry;

            public override void UpdateChain(IDetourFactory factory, Delegate? fallback)
            {
                if (nativeDetour is null || lastNativeEntry != SyncProxyNativeEntry)
                {
                    nativeDetour?.Dispose();
                    origDelegate = null;
                    nativeDetour = factory.CreateNativeDetour(Function, SyncProxyNativeEntry, applyByDefault: false);
                    lastNativeEntry = SyncProxyNativeEntry;
                }

                var del = Next?.EntryDelegate; // don't fall back to fallback, because that's the orig delegate
                del = del?.CastDelegate(EntryType);

                if (del is not null && !nativeDetour.IsApplied)
                {
                    nativeDetour.Apply();
                    origDelegate = null;
                }
                else if (del is null && nativeDetour.IsApplied)
                {
                    nativeDetour.Undo();
                    origDelegate = null;
                }

                SyncInfo.FirstDelegate = del;
            }
        }

        internal sealed class ChainDelegateState
        {
            public readonly Delegate Orig;
            public readonly Type NextType;
            public Delegate? Next;

            public ChainDelegateState(Delegate orig, Type nextType)
            {
                Orig = orig;
                NextType = nextType;
            }

            private static readonly FieldInfo ChainDelegateState_Orig = typeof(ChainDelegateState).GetField(nameof(Orig))!;
            private static readonly FieldInfo ChainDelegateState_Next = typeof(ChainDelegateState).GetField(nameof(Next))!;

            private static MethodInfo GenerateChainMethod(Type origDelType, Type nextDelType)
            {
                var origInvoke = origDelType.GetMethod("Invoke")!;
                var nextInvoke = nextDelType.GetMethod("Invoke")!;
                Helpers.Assert(origInvoke is not null && nextInvoke is not null);

                using var dmd = MethodSignature.ForMethod(nextInvoke, true).CreateDmd(DebugFormatter.Format($"Chain<{nextDelType}>"));
                Helpers.Assert(dmd.Module is not null && dmd.Definition is not null);
                var module = dmd.Module;
                dmd.Definition.Parameters.Insert(0, new(module.ImportReference(typeof(ChainDelegateState))));

                var il = dmd.GetILProcessor();

                // first, lets load the orig delegate
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, module.ImportReference(ChainDelegateState_Orig));
                // then load the next delegate, which is the first parameter to the orig delegate
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, module.ImportReference(ChainDelegateState_Next));
                // then we load the rest of our arguments
                for (var i = 1; i < dmd.Definition.Parameters.Count; i++)
                {
                    il.Emit(OpCodes.Ldarg, i);
                }
                // then we tail call to our orig delegate
                il.Emit(OpCodes.Tail);
                il.Emit(OpCodes.Callvirt, module.ImportReference(origInvoke));
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }

            private static readonly ConditionalWeakTable<Type, MethodInfo> chainMethodCache = new();
            private static MethodInfo GetChainMethod(Type origDelType, Type nextDelType)
            {
                // we can cache on only origDelType because technically, nextDelType is derived from origDelType
                return chainMethodCache.GetValue(origDelType, orig => GenerateChainMethod(orig, nextDelType));
            }

            private Delegate? selfDelegate;
            public Delegate GetDelegate()
            {
                return selfDelegate ??= GetChainMethod(Orig.GetType(), NextType).CreateDelegate(NextType, this);
            }

            public void Remove()
            {
                var nextInvoke = NextType.GetMethod("Invoke")!;
                Helpers.Assert(nextInvoke is not null);
                Next = GetRemovedStub(MethodSignature.ForMethod(nextInvoke)).CreateDelegate(NextType, null);
            }
        }
        #endregion

        internal sealed class NativeDetourState
        {
            public readonly IntPtr Function;

            public NativeDetourState(IntPtr function)
            {
                Function = function;
                detourList = new(function);
            }

            internal RootNativeDetourChainNode detourList;

            private readonly DepGraph<NativeChainNode> detourGraph = new();
            private NativeChainNode? noConfigChain;

            internal SpinLock detourLock = new(true);
            internal int detourChainVersion;

            public void AddDetour(SingleNativeDetourState detour, bool takeLock = true)
            {
                NativeDetourChainNode cnode;
                var lockTaken = false;
                try
                {
                    if (takeLock)
                        detourLock.Enter(ref lockTaken);
                    if (detour.ManagerData is not null)
                        throw new InvalidOperationException("Trying to add a detour which was already added");

                    cnode = new NativeDetourChainNode(detour);
                    detourChainVersion++;
                    if (cnode.Config is { } cfg)
                    {
                        var listNode = new DepListNode<NativeChainNode>(cfg, cnode);
                        var graphNode = new DepGraphNode<NativeChainNode>(listNode);

                        detourGraph.Insert(graphNode);

                        detour.ManagerData = graphNode;
                    }
                    else
                    {
                        cnode.Next = noConfigChain;
                        noConfigChain = cnode;

                        detour.ManagerData = cnode;
                    }

                    detourList.MaybeSetEntryType(detour.NativeDelegateType);

                    UpdateChain(detour.Factory);
                }
                finally
                {
                    if (lockTaken)
                        detourLock.Exit(true);
                }

                // TODO: make sure this ACTUALLY called outside of the lock
                InvokeDetourEvent(DetourManager.NativeDetourApplied, NativeDetourApplied, detour);
            }

            public void RemoveDetour(SingleNativeDetourState detour, bool takeLock = true)
            {
                NativeDetourChainNode cnode;
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

                        case DepGraphNode<NativeChainNode> gn:
                            RemoveGraphDetour(detour, gn);
                            cnode = (NativeDetourChainNode)gn.ListNode.ChainNode;
                            break;

                        case NativeDetourChainNode cn:
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
                InvokeDetourEvent(DetourManager.NativeDetourUndone, NativeDetourUndone, detour);
            }

            private void RemoveGraphDetour(SingleNativeDetourState detour, DepGraphNode<NativeChainNode> node)
            {
                detourGraph.Remove(node);
                UpdateChain(detour.Factory);
                node.ListNode.ChainNode.Remove();
            }

            private void RemoveNoConfigDetour(SingleNativeDetourState detour, NativeDetourChainNode node)
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

                UpdateChain(detour.Factory);
                node.Remove();
            }

            private void UpdateChain(IDetourFactory updatingFactory)
            {
                var graphNode = detourGraph.ListHead;

                NativeChainNode? chain = null;
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
                detourList.SyncInfo.WaitForNoActiveCalls(out _);
                try
                {
                    chain = detourList;
                    while (chain is not null)
                    {
                        // we want to use the factory for the next node first
                        var fac = (chain.Next as NativeDetourChainNode)?.Detour.Factory;
                        // then, if that doesn't exist, the current factory
                        fac ??= (chain as NativeDetourChainNode)?.Detour.Factory;
                        // and if that doesn't exist, then the updating factory
                        fac ??= updatingFactory;

                        chain.UpdateChain(fac, detourList.OrigDelegate);

                        chain = chain.Next;
                    }
                }
                finally
                {
                    Volatile.Write(ref detourList.SyncInfo.UpdatingThread, -1);
                }
            }

            private FunctionDetourInfo? info;
            public FunctionDetourInfo Info => info ??= new(this);

            public event Action<NativeDetourInfo>? NativeDetourApplied;
            public event Action<NativeDetourInfo>? NativeDetourUndone;

            private void InvokeDetourEvent(Action<NativeDetourInfo>? evt1, Action<NativeDetourInfo>? evt2, SingleNativeDetourState node)
            {
                if (evt1 is not null || evt2 is not null)
                {
                    var info = Info.GetDetourInfo(node);
                    evt1?.Invoke(info);
                    evt2?.Invoke(info);
                }
            }
        }

        internal sealed class SingleNativeDetourState : SingleDetourStateBase
        {
            public readonly IntPtr Function;
            public readonly Type NativeDelegateType;
            public readonly Delegate Invoker;
            public readonly bool HasOrigParam;

            public NativeDetourInfo? DetourInfo;

            public SingleNativeDetourState(INativeDetour detour) : base(detour)
            {
                Function = detour.Function;
                NativeDelegateType = detour.NativeDelegateType;
                Invoker = detour.Invoker;
                HasOrigParam = detour.HasOrigParam;
            }
        }

        private static readonly ConcurrentDictionary<IntPtr, NativeDetourState> nativeDetourStates = new();

        internal static NativeDetourState GetNativeDetourState(IntPtr function)
            => nativeDetourStates.GetOrAdd(function, static f => new(f));

        /// <summary>
        /// Gets the <see cref="FunctionDetourInfo"/> for a native function pointed to by <paramref name="function"/>.
        /// </summary>
        /// <param name="function">A pointer to the native function to get the <see cref="FunctionDetourInfo"/> of.</param>
        /// <returns>The <see cref="FunctionDetourInfo"/> for <paramref name="function"/>.</returns>
        public static FunctionDetourInfo GetNativeDetourInfo(IntPtr function)
            => GetNativeDetourState(function).Info;

        /// <summary>
        /// An event which is invoked whenever a <see cref="NativeHook"/> is applied.
        /// </summary>
        public static event Action<NativeDetourInfo>? NativeDetourApplied;
        /// <summary>
        /// An event which is invoked whenever a <see cref="NativeHook"/> is undone.
        /// </summary>
        public static event Action<NativeDetourInfo>? NativeDetourUndone;
    }
}
