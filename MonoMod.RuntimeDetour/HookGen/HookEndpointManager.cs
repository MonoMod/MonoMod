using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;

namespace MonoMod.RuntimeDetour.HookGen {
    public static class HookEndpointManager {

        private readonly static Dictionary<MethodBase, object> HookEndpointMap = new Dictionary<MethodBase, object>();
        private readonly static Dictionary<object, List<HookEntry>> OwnedHookLists = new Dictionary<object, List<HookEntry>>();

        public static event Func<AssemblyName, ModuleDefinition> OnGenerateCecilModule;
        internal static ModuleDefinition GenerateCecilModule(AssemblyName name)
            => OnGenerateCecilModule?.InvokeWhileNull<ModuleDefinition>(name);

        public static event Func<Delegate, object> OnGetOwner;
        private static object GetOwner(Delegate hook)
            => (OnGetOwner ?? DefaultOnGetOwner).InvokeWhileNull<object>(hook);
        private static object DefaultOnGetOwner(Delegate hook)
            => hook.Method.DeclaringType.Assembly;

        private static HookEndpoint GetEndpoint(MethodBase method) {
            HookEndpoint endpoint;
            if (HookEndpointMap.TryGetValue(method, out object endpointObj))
                endpoint = endpointObj as HookEndpoint;
            else
                HookEndpointMap[method] = endpoint = new HookEndpoint(method);

            return endpoint;
        }

        private static void AddEntry(HookEntryType type, MethodBase method, Delegate hook) {
            object owner = GetOwner(hook);
            if (owner == null)
                return;

            if (!OwnedHookLists.TryGetValue(owner, out List<HookEntry> list))
                OwnedHookLists[owner] = list = new List<HookEntry>();

            list.Add(new HookEntry() {
                Type = type,
                Method = method,
                Hook = hook
            });
        }

        private static void RemoveEntry(HookEntryType type, MethodBase method, Delegate hook) {
            object owner = GetOwner(hook);
            if (owner == null)
                return;

            if (!OwnedHookLists.TryGetValue(owner, out List<HookEntry> list))
                return;

            int index = list.FindLastIndex(entry => entry.Type.Equals(type) && entry.Method.Equals(method) && entry.Hook.Equals(hook));
            if (index == -1)
                return;
            list.RemoveAt(index);
        }

        public static void RemoveAllOwnedBy(object owner) {
            if (owner == null || !OwnedHookLists.TryGetValue(owner, out List<HookEntry> list) || list == null)
                return;
            OwnedHookLists.Remove(owner);

            foreach (HookEntry entry in list)
                switch (entry.Type) {
                    case HookEntryType.Hook:
                        GetEndpoint(entry.Method).Remove(entry.Hook);
                        break;

                    case HookEntryType.Modification:
                        GetEndpoint(entry.Method).Unmodify(entry.Hook);
                        break;
                }
        }

        // These must stay generic for backwards-compatibility.
        public static void Add<T>(MethodBase method, Delegate hookDelegate) where T : Delegate {
            GetEndpoint(method).Add(hookDelegate);
            AddEntry(HookEntryType.Hook, method, hookDelegate);
        }

        public static void Remove<T>(MethodBase method, Delegate hookDelegate) where T : Delegate {
            GetEndpoint(method).Remove(hookDelegate);
            RemoveEntry(HookEntryType.Hook, method, hookDelegate);
        }

        public static void Modify<T>(MethodBase method, Delegate callback) where T : Delegate {
            GetEndpoint(method).Modify(callback);
            AddEntry(HookEntryType.Modification, method, callback);
        }

        public static void Unmodify<T>(MethodBase method, Delegate callback) where T : Delegate {
            GetEndpoint(method).Unmodify(callback);
            RemoveEntry(HookEntryType.Modification, method, callback);
        }

        private class HookEntry {
            public HookEntryType Type;
            public MethodBase Method;
            public Delegate Hook;

            public override bool Equals(object obj)
                => obj is HookEntry other &&
                    other.Type.Equals(Type) &&
                    other.Method.Equals(Method) &&
                    other.Hook.Equals(Hook);

            public override int GetHashCode()
                => Type.GetHashCode() ^ Method.GetHashCode() ^ Hook.GetHashCode();
        }

        private enum HookEntryType {
            Hook,
            Modification
        }

    }
}
