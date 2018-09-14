using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;

namespace MonoMod.RuntimeDetour.HookGen {
    public static class HookEndpointManager {

        private static Dictionary<MethodBase, object> HookMap = new Dictionary<MethodBase, object>();

        private static Dictionary<Assembly, ModuleDefinition> ModuleMap = new Dictionary<Assembly, ModuleDefinition>();
        private static Dictionary<Assembly, bool> ModuleManagedMap = new Dictionary<Assembly, bool>();

        internal static ModuleDefinition GetModule(Assembly asm) {
            ModuleDefinition module;
            if (!ModuleMap.TryGetValue(asm, out module) || module == null) {
                ModuleMap[asm] = module = ModuleDefinition.ReadModule(asm.Location);
                ModuleManagedMap[asm] = true;
            }
            return module;
        }

        public static void SetModule(Assembly asm, ModuleDefinition module) {
            if (ModuleManagedMap.TryGetValue(asm, out bool isManaged) && isManaged) {
                ModuleMap[asm].Dispose();
            }
            ModuleMap[asm] = module;
            ModuleManagedMap[asm] = false;
        }

        internal static HookEndpoint<T> Get<T>(MethodBase method) where T : Delegate {
            HookEndpoint<T> endpoint;
            if (HookMap.TryGetValue(method, out object endpointObj))
                endpoint = endpointObj as HookEndpoint<T>;
            else
                HookMap[method] = endpoint = new HookEndpoint<T>(method);

            return endpoint;
        }

        public static void Add<T>(MethodBase method, Delegate hookDelegate) where T : Delegate {
            Get<T>(method).Add(hookDelegate);
        }

        public static void Remove<T>(MethodBase method, Delegate hookDelegate) where T : Delegate {
            Get<T>(method).Remove(hookDelegate);
        }

        public static void Modify<T>(MethodBase method, Delegate callback) where T : Delegate {
            Get<T>(method).Modify(callback);
        }

        public static void Unmodify<T>(MethodBase method, Delegate callback) where T : Delegate {
            Get<T>(method).Unmodify(callback);
        }

    }
}
