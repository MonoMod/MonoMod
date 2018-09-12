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
        private static ulong ID = 0;

        internal static HookEndpoint<T> Verify<T>(HookEndpoint<T> endpoint) where T : class {
            HookEndpoint<T> lastEndpoint = null;
            if (HookMap.TryGetValue(endpoint.Method, out object endpointObj))
                lastEndpoint = endpointObj as HookEndpoint<T>;
            if (lastEndpoint == null)
                throw new InvalidOperationException("Invalid hook endpoint usage");
            if (lastEndpoint.ID != endpoint.ID)
                throw new ArgumentException("Endpoint out of sync - don't store or reuse them");

            endpoint = new HookEndpoint<T>(endpoint) {
                ID = ID++
            };
            HookMap[endpoint.Method] = endpoint;
            return endpoint;
        }

        public static HookEndpoint<T> Get<T>(MethodBase method) where T : class {
            HookEndpoint<T> endpoint;
            if (HookMap.TryGetValue(method, out object endpointObj))
                endpoint = endpointObj as HookEndpoint<T>;
            else
                HookMap[method] = endpoint = new HookEndpoint<T>(method);

            endpoint.ID = ID++;
            return endpoint;
        }

        public static void Set<T>(MethodBase method, HookEndpoint<T> endpoint) where T : class {
            if (endpoint == null)
                throw new ArgumentNullException("Cannot set a hook endpoint to null");
            if (endpoint.Method != method)
                throw new ArgumentException("Cannot mix and match hook endpoints");

            Verify(endpoint).ApplyQueue();
        }

        [Obsolete("Use Get / Set instead!")]
        public static void Add<T>(MethodBase method, Delegate hookDelegate) where T : class {
            HookEndpoint<T> endpoint = Get<T>(method);
            endpoint._Add(hookDelegate);
            Set<T>(method, endpoint);
        }

        [Obsolete("Use Get / Set instead!")]
        public static void Remove<T>(MethodBase method, Delegate hookDelegate) where T : class {
            HookEndpoint<T> endpoint = Get<T>(method);
            endpoint._Remove(hookDelegate);
            Set<T>(method, endpoint);
        }

    }
}
