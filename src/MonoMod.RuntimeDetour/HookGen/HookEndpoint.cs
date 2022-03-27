using System;
using System.Reflection;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using MonoMod.Cil;

namespace MonoMod.RuntimeDetour.HookGen {
    internal sealed class HookEndpoint {

        internal readonly MethodBase Method;

        private readonly Dictionary<Delegate, Stack<IDetour>> HookMap = new Dictionary<Delegate, Stack<IDetour>>();
        private readonly List<IDetour> HookList = new List<IDetour>();

        internal HookEndpoint(MethodBase method) {
            Method = method;
        }

        private static IDetour _NewHook(MethodBase from, Delegate to) => new Hook(from, to);
        private static IDetour _NewILHook(MethodBase from, ILContext.Manipulator to) => new ILHook(from, to);

        private void _Add<TDelegate>(Func<MethodBase, TDelegate, IDetour> gen, TDelegate hookDelegate) where TDelegate : Delegate {
            if (hookDelegate == null)
                return;

            if (!HookMap.TryGetValue(hookDelegate, out Stack<IDetour> hooks))
                HookMap[hookDelegate] = hooks = new Stack<IDetour>();

            IDetour hook = gen(Method, hookDelegate);
            hooks.Push(hook);
            HookList.Add(hook);
        }

        public void _Remove(Delegate hookDelegate) {
            if (hookDelegate == null)
                return;

            // Note: A hook delegate can be applied multiple times.
            // The following code removes the last hook of that delegate type.
            if (!HookMap.TryGetValue(hookDelegate, out Stack<IDetour> hooks))
                return;

            IDetour hook = hooks.Pop();
            hook.Dispose();

            if (hooks.Count == 0)
                HookMap.Remove(hookDelegate);

            HookList.Remove(hook);
        }

        public void Add(Delegate hookDelegate) {
            _Add(_NewHook, hookDelegate);
        }

        public void Remove(Delegate hookDelegate) {
            _Remove(hookDelegate);
        }

        public void Modify(Delegate hookDelegate) {
            _Add(_NewILHook, hookDelegate.CastDelegate<ILContext.Manipulator>());
        }

        public void Unmodify(Delegate hookDelegate) {
            _Remove(hookDelegate);
        }

    }
}
