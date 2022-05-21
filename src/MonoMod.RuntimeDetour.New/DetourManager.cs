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
                Helpers.Assert((Next is null) == (trampolineDetour is null));
            }

            public void InsertAfter(IDetourFactory factory, Node newNext, MethodBase fallback) {
                Assert();

                throw new NotImplementedException();

                Assert();
            }
        }

        private sealed class DetourNode : Node {
            public readonly IDetour Detour;

            public DetourNode(IDetour detour) {
                Detour = detour;
            }

            public override MethodBase Entry => Detour.InvokeTarget;
            public override MethodBase NextTrampoline => Detour.NextTrampoline;
        }

        internal class DetourState {
            public readonly MethodBase Source;
            public readonly MethodBase ILCopy;

            public DetourState(MethodBase src) {
                Source = src;
                ILCopy = src.CreateILCopy();
                detourList = new(src);
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

            private readonly RootNode detourList;

            public void AddDetour(IDetourFactory factory, IDetour detour) {
                lock (detourList) {

                    Node node = detourList;
                    while (node.Next is not null) {
                        // TODO: stop when we've found where we want to be

                        node = node.Next;
                    }

                    var newNode = new DetourNode(detour);
                    node.InsertAfter(factory, newNode, ILCopy);
                }
            }
        }

        private static ConcurrentDictionary<MethodBase, DetourState> detourStates = new();

        public static DetourState GetDetourState(MethodBase method)
            => detourStates.GetOrAdd(method, m => new(m));
    }
}
