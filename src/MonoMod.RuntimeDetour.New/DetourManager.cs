using MonoMod.Core;
using MonoMod.Core.Utils;
using MonoMod.RuntimeDetour.Utils;
using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    internal static class DetourManager {

        private abstract class Node {

            public Node? Next { get; private set; }

            public abstract MethodBase Entry { get; }
            public abstract MethodBase NextTrampoline { get; }

            private ICoreDetour? trampolineDetour;

            private void Assert() {
                if (Next is not null)
                    Helpers.Assert(trampolineDetour is not null);
            }

            private void UpdateDetours(IDetourFactory factory, MethodBase fallback) {
                var detourSrc = NextTrampoline;
                var detourTarget = Next?.Entry;
                if (detourTarget is not null || (detourTarget is null && this is not RootNode)) {
                    trampolineDetour = factory.CreateDetour(detourSrc, detourTarget ?? fallback, applyByDefault: true);
                }
            }

            private static void UnlinkDetour(ref ICoreDetour? trampolineDetour) {
                if (trampolineDetour is not null) {
                    trampolineDetour.Undo();
                    trampolineDetour.Dispose();
                    trampolineDetour = null;
                }
            }

            public void InsertAfter(IDetourFactory factory, Node newNext, MethodBase fallback) {
                Assert();
                Next?.Assert();

                // TODO: do something to minimize the amount of time a detour isn't present

                // first, we unhook this trampoline detour
                UnlinkDetour(ref trampolineDetour);

                // then update our linked list, and update the detours for it
                newNext.Next = Next;
                Next = newNext;

                UpdateDetours(factory, fallback);
                Next.UpdateDetours(factory, fallback);

                Assert();
                Next?.Assert();
            }

            public void RemoveNext(IDetourFactory factory, MethodBase fallback) {
                Assert();
                Next?.Assert();

                var oldNext = Next;
                Helpers.DAssert(oldNext is not null);
                Next = oldNext.Next;

                // break the link to the old next node
                UnlinkDetour(ref trampolineDetour);
                // then update our own detour to target the new next method
                UpdateDetours(factory, fallback);

                // now the detour chain is correct, we just want to clean up oldNext
                UnlinkDetour(ref oldNext.trampolineDetour);

                Assert();
                Next?.Assert();
            }
        }

        private sealed class DetourNode : Node {
            public DetourNode(IDetour detour) {
                Entry = detour.InvokeTarget;
                NextTrampoline = detour.NextTrampoline;
            }

            public override MethodBase Entry { get; }
            public override MethodBase NextTrampoline { get; }
        }

        // The root node is the existing method. It's NextTrampoline is the method, which is the same
        // as the entry point, because we want to detour the entry point. Entry should never be targeted though.
        private sealed class RootNode : Node {
            public override MethodBase Entry => NextTrampoline;
            public override MethodBase NextTrampoline { get; }

            public RootNode(MethodBase method) {
                NextTrampoline = method;
            }
        }

        internal class DetourState {
            public readonly MethodBase Source;
            public readonly MethodBase ILCopy;

            public DetourState(MethodBase src) {
                Source = src;
                ILCopy = src.CreateILCopy();
                detourList = new(src);
            }

            private readonly RootNode detourList;

            public void AddDetour(IDetourFactory factory, IDetour detour) {
                lock (detourList) {
                    Node node = detourList;
                    while (node.Next is not null) {
                        // TODO: stop when we've found where we want to be

                        node = node.Next;
                    }

             
                    var newNode = new DetourNode(detour);
                    detour.ManagerData = newNode;
                    node.InsertAfter(factory, newNode, ILCopy);
                }
            }

            public void RemoveDetour(IDetourFactory factory, IDetour detour) {
                lock (detourList) {
                    Node node = detourList;
                    var mgrData = detour.ManagerData;
                    while (node.Next is not null) {
                        if (ReferenceEquals(node.Next, mgrData))
                            break;

                        node = node.Next;
                    }

                    detour.ManagerData = null;
                    node.RemoveNext(factory, ILCopy);
                }
            }
        }

        private static ConcurrentDictionary<MethodBase, DetourState> detourStates = new();

        public static DetourState GetDetourState(MethodBase method)
            => detourStates.GetOrAdd(method, m => new(m));
    }
}
