using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.RuntimeDetour.HookGen {

    /// <summary>
    /// Provided for back-compat with old versions of HookGen
    /// </summary>
    public static class HookEndpointManager {

        private static Dictionary<(MethodBase, Delegate), Hook> Hooks = new();
        private static Dictionary<(MethodBase, Delegate), ILHook> ILHooks = new();

        // Both generic and non-generic variants must stay for backwards-compatibility.
        public static void Add<T>(MethodBase method, Delegate hookDelegate) where T : Delegate => Add(method, hookDelegate);
        public static void Add(MethodBase method, Delegate hookDelegate) {
            Hooks.Add((method, hookDelegate), new Hook(method, hookDelegate));
        }

        public static void Remove<T>(MethodBase method, Delegate hookDelegate) where T : Delegate => Remove(method, hookDelegate);
        public static void Remove(MethodBase method, Delegate hookDelegate) {
            if (Hooks.TryGetValue((method, hookDelegate), out var hook)) {
                Hooks.Remove((method, hookDelegate));
                hook.Dispose();
            }
        }

        public static void Modify<T>(MethodBase method, Delegate callback) where T : Delegate => Modify(method, callback);
        public static void Modify(MethodBase method, Delegate callback) {
            ILHooks.Add((method, callback), new ILHook(method, (ILContext.Manipulator)callback));
        }

        public static void Unmodify<T>(MethodBase method, Delegate callback)  => Unmodify(method, callback);
        public static void Unmodify(MethodBase method, Delegate callback) {
            if (ILHooks.TryGetValue((method, callback), out var hook)) {
                ILHooks.Remove((method, callback));
                hook.Dispose();
            }
        }

        public static void Clear() {
            foreach (var hook in Hooks.Values)
                hook.Dispose();

            Hooks.Clear();

            foreach (var hook in ILHooks.Values)
                hook.Dispose();

            ILHooks.Clear();
        }
    }
}
