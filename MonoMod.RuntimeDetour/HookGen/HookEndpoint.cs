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
        private readonly List<Hook> HookList = new List<Hook>();
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
        private DynamicMethod ILManipulated;

        internal HookEndpoint(MethodBase method) {
            Method = method;
        }

        internal void UpdateILManipulated(bool force = false) {
            if (force || ILList.Count != 0)
                ILManipulated = DMD.Generate();
            else
                ILManipulated = null;

            if (HookList.Count != 0)
                HookList[0].UpdateOrig(ILManipulated);
        }

        public void Add(Delegate hookDelegate) {
            if (hookDelegate == null)
                return;

            Stack<Hook> hooks;
            if (!HookMap.TryGetValue(hookDelegate, out hooks))
                HookMap[hookDelegate] = hooks = new Stack<Hook>();

            Hook hook = new Hook(Method, hookDelegate);
            hooks.Push(hook);
            if (HookList.Count == 0)
                hook.UpdateOrig(ILManipulated);
            HookList.Add(hook);
        }

        public void Remove(Delegate hookDelegate) {
            if (hookDelegate == null)
                return;

            // Note: A hook delegate can be applied multiple times.
            // The following code removes the last hook of that delegate type.
            Stack<Hook> hooks;
            if (!HookMap.TryGetValue(hookDelegate, out hooks))
                return;

            Hook hook = hooks.Pop();
            hook.Dispose();

            if (hooks.Count == 0)
                HookMap.Remove(hookDelegate);

            int index = HookList.IndexOf(hook);
            HookList.RemoveAt(index);
            if (index == 0 && HookList.Count != 0)
                HookList[0].UpdateOrig(ILManipulated);
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

            UpdateILManipulated(true);

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

            UpdateILManipulated();
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
                return !hookIL._ReadOnly;
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
