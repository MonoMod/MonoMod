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

        private Detour ILDetour;
        private static DetourConfig ILDetourConfig = new DetourConfig() {
            Priority = int.MinValue / 8
        };

        internal HookEndpoint(MethodBase method) {
            Method = method;
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
        }

        public void Modify(Delegate callback) {
            if (callback == null)
                return;

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(Method, HookEndpointManager.GenerateCecilModule)) {
                MethodDefinition def = dmd.Definition;
                try {
                    foreach (Delegate cb in ILList)
                        InvokeManipulator(def, cb);

                    if (!InvokeManipulator(def, callback))
                        return;

                    UpdateILManipulatedDetour(dmd.Generate());
                } catch {
                    _ManipulatorFailure(dmd, true);
                    throw;
                }
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

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(Method, HookEndpointManager.GenerateCecilModule)) {
                MethodDefinition def = dmd.Definition;
                try {
                    foreach (Delegate cb in ILList)
                        InvokeManipulator(def, cb);

                    UpdateILManipulatedDetour(dmd.Generate());
                } catch {
                    _ManipulatorFailure(dmd, false);
                    throw;
                }
            }
        }

        private void UpdateILManipulatedDetour(MethodBase target) {
            ILDetour?.Dispose();
            if (target != null)
                ILDetour = new Detour(Method, target, ref ILDetourConfig);
            else
                ILDetour = null;
        }

        private bool _ManipulatorFailure(DynamicMethodDefinition dmd, bool reapply) {
            if (reapply) {
                try {
                    dmd.Reload();
                    MethodDefinition def = dmd.Definition;
                    foreach (Delegate cb in ILList)
                        InvokeManipulator(def, cb);

                    UpdateILManipulatedDetour(dmd.Generate());
                } catch {
                    _ManipulatorFailure(dmd, false);
                    throw;
                }

            } else {
                UpdateILManipulatedDetour(null);
            }

            return false;
        }

        private bool InvokeManipulator(MethodDefinition def, Delegate cb) {
            if (cb.TryCastDelegate(out ILContext.Manipulator manip)) {
                // The callback is an ILManipulator, or compatible to it out of the box.
                ILContext il = new ILContext(def);
                il.ReferenceBag = RuntimeILReferenceBag.Instance;
                il.Invoke(manip);
                if (il.IsReadOnly) {
                    il.Dispose();
                    return false;
                }

                // Free the now useless MethodDefinition and ILProcessor references.
                // This also prevents clueless people from storing the ILContext elsewhere
                // and reusing it outside of the IL manipulation context.
                il.MakeReadOnly();
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
                if (t_hookIL.GetField("_ReadOnly", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hookIL) as bool? ?? false) {
                    (hookIL as IDisposable)?.Dispose();
                    return false;
                }

                // TODO: Free the underlying MethodDefinition ref.
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
