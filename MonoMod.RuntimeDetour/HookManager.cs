using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;

namespace MonoMod.RuntimeDetour {
    /// <summary>
    /// Used by MonoMod.RuntimeDetour.HookGen.
    /// </summary>
    public static class HookManager {

        private static Dictionary<HookKey, Stack<Hook>> _HookMap = new Dictionary<HookKey, Stack<Hook>>();

        public static void Add(MethodBase method, Delegate hookDelegate) {
            HookKey key = new HookKey(method, hookDelegate);
            Stack<Hook> hooks;
            if (!_HookMap.TryGetValue(key, out hooks))
                _HookMap[key] = hooks = new Stack<Hook>();

            Hook hook = new Hook(method, hookDelegate);

            hooks.Push(hook);
        }

        public static void Remove(MethodBase method, Delegate hookDelegate) {
            HookKey key = new HookKey(method, hookDelegate);
            Stack<Hook> hooks;
            if (!_HookMap.TryGetValue(key, out hooks))
                throw new KeyNotFoundException($"No hooks for {method} -> {hookDelegate} found");

            hooks.Pop().Undo();

            if (hooks.Count == 0)
                _HookMap.Remove(key);
        }

        private struct HookKey {
            public MethodBase Method;
            public Delegate Hook;

            public HookKey(MethodBase method, Delegate hook) {
                Method = method;
                Hook = hook;
            }

            public override int GetHashCode() {
                return Method.GetHashCode() ^ Hook.GetHashCode();
            }

            public override bool Equals(object obj) {
                if (!(obj is HookKey))
                    return false;
                HookKey other = (HookKey) obj;
                return ReferenceEquals(Method, other.Method) && ReferenceEquals(Hook, other.Hook);
            }

            public override string ToString() {
                return $"[HookKey ({Method}) ({Hook})]";
            }
        }

        private class HookKeyEqualityComparer : EqualityComparer<HookKey> {
            public override bool Equals(HookKey x, HookKey y) {
                return ReferenceEquals(x.Method, y.Method) && ReferenceEquals(x.Hook, y.Hook);
            }

            public override int GetHashCode(HookKey obj) {
                return obj.Method.GetHashCode() ^ obj.Hook.GetHashCode();
            }
        }

    }
}
