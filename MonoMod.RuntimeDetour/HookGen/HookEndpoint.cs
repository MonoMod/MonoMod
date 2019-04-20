using System;
using System.Reflection;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using MonoMod.Cil;

namespace MonoMod.RuntimeDetour.HookGen {
    internal sealed class HookEndpoint {

        internal readonly MethodBase Method;

        private readonly Dictionary<Delegate, Stack<Hook>> HookMap = new Dictionary<Delegate, Stack<Hook>>();
        private readonly List<Hook> HookList = new List<Hook>();
        private readonly List<Delegate> ILList = new List<Delegate>();
        private readonly List<IDisposable> ActiveMMILs = new List<IDisposable>();

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
        private MethodBase ILManipulated;
        private Detour ILDetour;

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
            else {
                ILDetour?.Dispose();
                if (ILManipulated != null)
                    ILDetour = new Detour(Method, ILManipulated);
            }
        }

        public void Add(Delegate hookDelegate) {
            if (hookDelegate == null)
                return;

            if (!HookMap.TryGetValue(hookDelegate, out Stack<Hook> hooks))
                HookMap[hookDelegate] = hooks = new Stack<Hook>();

            if (HookList.Count == 0) {
                ILDetour?.Dispose();
                ILDetour = null;
            }

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
            if (!HookMap.TryGetValue(hookDelegate, out Stack<Hook> hooks))
                return;

            Hook hook = hooks.Pop();
            hook.Dispose();

            if (hooks.Count == 0)
                HookMap.Remove(hookDelegate);

            int index = HookList.IndexOf(hook);
            HookList.RemoveAt(index);
            if (index == 0) {
                if (HookList.Count != 0) {
                    HookList[0].UpdateOrig(ILManipulated);
                } else if (ILManipulated != null) {
                    ILDetour = new Detour(Method, ILManipulated);
                }
            }
        }

        public void Modify(Delegate callback) {
            if (callback == null)
                return;

            try {
                if (!InvokeManipulator(DMD.Definition, callback))
                    return;

                UpdateILManipulated(true);
            } catch when (_ManipulatorFailure(true)) {
                throw;
            }

            ILList.Add(callback);

            HookEndpointManager.InvokeOnPostModify(Method, callback);
        }

        public void Unmodify(Delegate callback) {
            if (callback == null)
                return;

            int index = ILList.LastIndexOf(callback);
            if (index == -1)
                return;
            ILList.RemoveAt(index);

            foreach (IDisposable h in ActiveMMILs)
                h.Dispose();
            ActiveMMILs.Clear();

            DMD.Reload();
            MethodDefinition def = DMD.Definition;
            try {
                foreach (Delegate cb in ILList)
                    InvokeManipulator(def, cb);

                UpdateILManipulated();
            } catch when (_ManipulatorFailure(false)) {
                throw;
            }
        }

        private bool _ManipulatorFailure(bool reapply) {
            DMD.Reload();

            if (reapply) {
                try {
                    if (reapply) {
                        MethodDefinition def = DMD.Definition;
                        foreach (Delegate cb in ILList)
                            InvokeManipulator(def, cb);
                    }

                    UpdateILManipulated();
                } catch when (_ManipulatorFailure(false)) {
                    throw;
                }

            } else {
                UpdateILManipulated();
            }

            return false;
        }

        private bool InvokeManipulator(MethodDefinition def, Delegate cb) {
            if (cb.TryCastDelegate(out ILContext.Manipulator manip)) {
                // The callback is an ILManipulator, or compatible to it out of the box.
                ILContext il = new ILContext(def);
                il.ReferenceBag = RuntimeILReferenceBag.Instance;
                il.Invoke(manip);
                if (il.IsReadOnly)
                    return false;

                ActiveMMILs.Add(il);
                return true;
            }

            // Check if the method accepts a HookIL from another assembly.
            ParameterInfo[] args = cb.GetMethodInfo().GetParameters();
            if (args.Length == 1 && args[0].ParameterType.FullName == typeof(ILContext).FullName) {
                // Instantiate it. We should rather pass a "proxy" of some sorts, but eh.
                object hookIL = args[0].ParameterType.GetConstructors()[0].Invoke(new object[] { def });
                Type t_hookIL = hookIL.GetType();
                // TODO: Set the reference bag.
                t_hookIL.GetMethod("Invoke").Invoke(hookIL, new object[] { cb });
                if (t_hookIL.GetField("_ReadOnly", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hookIL) as bool? ?? false)
                    return false;

                if (hookIL is IDisposable disp)
                    ActiveMMILs.Add(disp);
                return true;
            }

            // Fallback - body and IL processor.
            cb.DynamicInvoke(def.Body, def.Body.GetILProcessor());
            def.ConvertShortLongOps();

            return true;
        }

    }
}
