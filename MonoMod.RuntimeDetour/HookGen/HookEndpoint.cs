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
    internal sealed class HookEndpoint {

        internal readonly MethodBase Method;

        private readonly Dictionary<Delegate, Stack<Hook>> HookMap = new Dictionary<Delegate, Stack<Hook>>();
        private readonly List<Delegate> ILList = new List<Delegate>();

        private DynamicMethodDefinition _DMD;
        private DynamicMethodDefinition DMD {
            get {
                lock (HookMap) {
                    if (_DMD != null)
                        return _DMD;

                    // Note: This can but shouldn't fail, mainly if the user hasn't provided a Cecil ModuleDefinition generator.
                    return _DMD = new DynamicMethodDefinition(Method, HookEndpointManager.GenerateCecilModule);
                }
            }
        }
        private DynamicMethod ILCopy;
        private NativeDetour ILProxyDetour;
        private Detour ILDetour;

        internal HookEndpoint(MethodBase method) {
            Method = method;
            
            // Add a "transparent" detour for IL manipulation.

            bool hasMethodBody;
            try {
                hasMethodBody = (method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0) != 0;
            } catch {
                hasMethodBody = false;
            }

            if (hasMethodBody) {
                ILCopy = method.CreateILCopy();
                ILDetour = new Detour(method, ILCopy);
                DetourILDetourTarget();
            }
        }

        internal void DetourILDetourTarget(bool force = false) {
            ILProxyDetour?.Dispose();
            ILProxyDetour = null;
            if (!force && ILList.Count == 0)
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

            try {
                if (!InvokeManipulator(DMD.Definition, callback))
                    return;
            } catch when (_ManipulatorFailure()) {
                throw;
            }

            DetourILDetourTarget(true);

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
            try {
                foreach (Delegate cb in ILList)
                    InvokeManipulator(def, cb);
            } catch when(_ManipulatorFailure()) {
                throw;
            }

            DMD.Definition.RecalculateILOffsets();
            DMD.Definition.ConvertShortLongOps();
            DetourILDetourTarget();
        }

        private bool _ManipulatorFailure() {
            DMD.Reload(null, true);
            return false;
        }

        private static bool InvokeManipulator(MethodDefinition def, Delegate cb) {
            if (cb.TryCastDelegate(out ILManipulator manip)) {
                // The callback is an ILManipulator, or compatible to it out of the box.
                HookIL hookIL = new HookIL(def);
                hookIL.Invoke(manip);
                return !hookIL.ReadOnly;
            }

            // Check if the method accepts a HookIL from another assembly.
            ParameterInfo[] args = cb.Method.GetParameters();
            if (args.Length == 1 && args[0].ParameterType.FullName == typeof(HookIL).FullName) {
                // Instantiate it. We should rather pass a "proxy" of some sorts, but eh.
                object hookIL = args[0].ParameterType.GetConstructors()[0].Invoke(new object[] { def });
                Type t_hookIL = hookIL.GetType();
                t_hookIL.GetMethod("Invoke").Invoke(hookIL, new object[] { cb });
                return !(t_hookIL.GetField("ReadOnly")?.GetValue(hookIL) as bool? ?? false);
            }

            // Fallback - body and IL processor.
            cb.DynamicInvoke(def.Body, def.Body.GetILProcessor());
            def.RecalculateILOffsets();
            def.ConvertShortLongOps();

            return true;
        }

    }
}
