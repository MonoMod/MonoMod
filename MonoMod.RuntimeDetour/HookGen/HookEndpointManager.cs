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

        public static event Func<AssemblyName, ModuleDefinition> OnGenerateCecilModule;
        internal static ModuleDefinition GenerateCecilModule(AssemblyName name)
            => OnGenerateCecilModule(name);

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
