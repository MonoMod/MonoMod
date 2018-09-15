using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using System.Text;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour.HookGen {
    internal sealed class HookEndpoint<T> where T : Delegate {

        internal readonly MethodBase Method;

        private readonly Dictionary<Delegate, Stack<Hook>> HookMap = new Dictionary<Delegate, Stack<Hook>>();
        private readonly List<Delegate> ILList = new List<Delegate>();

        private DynamicMethodDefinition DMD;
        private DynamicMethod ILCopy;
        private NativeDetour ILProxyDetour;
        private Detour ILDetour;

        internal HookEndpoint(MethodBase method) {
            Method = method;

            try {
                // Add a "transparent" detour for IL manipulation.
                DMD = new DynamicMethodDefinition(method, HookEndpointManager.GetModule(method.DeclaringType.Assembly));
                ILCopy = method.CreateILCopy();
                ILDetour = new Detour(method, ILCopy);
                DetourILDetourTarget();
            } catch {
                // Fail silently.
            }
        }

        internal void DetourILDetourTarget() {
            ILProxyDetour?.Dispose();
            if (ILList.Count == 0)
                return;
            try {
                ILProxyDetour = new NativeDetour(ILCopy, DMD.Generate());
            } catch (Exception e) {
                StringBuilder builder = new StringBuilder();
                if (DMD.Definition?.Body?.Instructions != null) {
                    builder.AppendLine("IL hook failed for:");
                    foreach (Instruction i in DMD.Definition.Body.Instructions)
                        builder.AppendLine(i?.ToString() ?? "NULL!");
                } else {
                    builder.AppendLine("IL hook failed, no instructions found");
                }
                throw new InvalidProgramException(builder.ToString(), e);
            }
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

            MethodDefinition def = DMD.Definition;
            callback.DynamicInvoke(def.Body, def.Body.GetILProcessor());

            DMD.Definition.RecalculateILOffsets();
            DMD.Definition.ConvertShortLongOps();
            DetourILDetourTarget();

            ILList.Add(callback);
        }

        public void Unmodify(Delegate callback) {
            if (callback == null)
                return;

            int index = ILList.LastIndexOf(callback);
            if (index == -1)
                return;
            ILList.RemoveAt(index);

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
