using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;

namespace MonoMod.RuntimeDetour.HookGen {
    internal sealed class HookEndpoint<T> where T : Delegate {

        internal readonly MethodBase Method;

        private readonly Dictionary<Delegate, Stack<Hook>> HookMap = new Dictionary<Delegate, Stack<Hook>>();
        private readonly List<Delegate> ILList = new List<Delegate>();

        private DynamicMethodDefinition DMD;
        private DynamicMethod ILCopy;
        private DynamicMethod ILProxy;
        private NativeDetour ILProxyDetour;
        private Detour ILDetour;

        internal HookEndpoint(MethodBase method) {
            Method = method;

            try {
                // Add a "transparent" detour for IL manipulation.
                DMD = new DynamicMethodDefinition(method, HookEndpointManager.GetModule(method.DeclaringType.Assembly));
                ILCopy = method.CreateILCopy();

                ParameterInfo[] args = Method.GetParameters();
                Type[] argTypes;
                if (!Method.IsStatic) {
                    argTypes = new Type[args.Length + 1];
                    argTypes[0] = Method.DeclaringType;
                    for (int i = 0; i < args.Length; i++)
                        argTypes[i + 1] = args[i].ParameterType;
                } else {
                    argTypes = new Type[args.Length];
                    for (int i = 0; i < args.Length; i++)
                        argTypes[i] = args[i].ParameterType;
                }

                ILProxy = new DynamicMethod(
                    "ILDetour:" + DMD.Definition.DeclaringType.FullName + "::" + DMD.Definition.Name,
                    (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                    Method.DeclaringType,
                    false
                ).Stub().Pin();

                ILDetour = new Detour(method, ILProxy);

                DetourILDetourTarget();
            } catch {
                // Fail silently.
            }
        }

        internal void DetourILDetourTarget() {
            ILProxyDetour?.Dispose();
            ILProxyDetour = new NativeDetour(ILProxy, ILList.Count == 0 ? ILCopy : DMD.Generate());
        }

        public void Add(Delegate hookDelegate) {
            if (hookDelegate == null)
                return;

            Stack<Hook> hooks;
            if (!HookMap.TryGetValue(hookDelegate, out hooks))
                HookMap[hookDelegate] = hooks = new Stack<Hook>();

            hooks.Push(new Hook(Method, hookDelegate));
        }

        public void Remove(Delegate hookDelegate) {
            if (hookDelegate == null)
                return;

            // Note: A hook delegate can be applied multiple times.
            // The following code removes the last hook of that delegate type.
            Stack<Hook> hooks;
            if (!HookMap.TryGetValue(hookDelegate, out hooks))
                return;

            hooks.Pop().Dispose();

            if (hooks.Count == 0)
                HookMap.Remove(hookDelegate);
        }

        public void Modify(Delegate callback) {
            if (callback == null)
                return;

            ILList.Add(callback);
            MethodDefinition def = DMD.Definition;
            callback.DynamicInvoke(def.Body, def.Body.GetILProcessor());

            DMD.Definition.RecalculateILOffsets();
            DMD.Definition.ConvertShortLongOps();
            DetourILDetourTarget();
        }

        public void Unmodify(Delegate callback) {
            if (callback == null)
                return;

            ILList.Remove(callback);
            DMD.Reload(null, true);
            MethodDefinition def = DMD.Definition;
            foreach (Delegate cb in ILList)
                cb.DynamicInvoke(def.Body, def.Body.GetILProcessor());

            DMD.Definition.RecalculateILOffsets();
            DMD.Definition.ConvertShortLongOps();
            DetourILDetourTarget();
        }

    }
}
