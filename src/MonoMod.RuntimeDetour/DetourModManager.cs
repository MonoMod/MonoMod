using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour {
    public sealed class DetourModManager : IDisposable {

        private readonly Dictionary<IDetour, Assembly> DetourOwners = new Dictionary<IDetour, Assembly>();
        private readonly Dictionary<Assembly, List<IDetour>> OwnedDetourLists = new Dictionary<Assembly, List<IDetour>>();

        public HashSet<Assembly> Ignored = new HashSet<Assembly>();
        
        // events for tracking/logging/rejecting
        public event Action<Assembly, MethodBase, ILContext.Manipulator> OnILHook;
        public event Action<Assembly, MethodBase, MethodBase, object> OnHook;
        public event Action<Assembly, MethodBase, MethodBase> OnDetour;
        public event Action<Assembly, MethodBase, IntPtr, IntPtr> OnNativeDetour;

        public DetourModManager() {
            Ignored.Add(typeof(DetourModManager).Assembly);

            // Keep track of all NativeDetours, Detours, Hooks and ILHooks.
            // Hooks have a 1:1 correspondence to Detours, but tracking the 'top level' construct
            // provides better future guarantees and logging
            ILHook.OnDetour += RegisterILHook;
            ILHook.OnUndo += UnregisterDetour;
            Hook.OnDetour += RegisterHook;
            Hook.OnUndo += UnregisterDetour;
            Detour.OnDetour += RegisterDetour;
            Detour.OnUndo += UnregisterDetour;
            NativeDetour.OnDetour += RegisterNativeDetour;
            NativeDetour.OnUndo += UnregisterDetour;
        }

        private bool Disposed;
        public void Dispose() {
            if (Disposed)
                return;
            Disposed = true;

            OwnedDetourLists.Clear();

            ILHook.OnDetour -= RegisterILHook;
            ILHook.OnUndo -= UnregisterDetour;
            Hook.OnDetour -= RegisterHook;
            Hook.OnUndo -= UnregisterDetour;
            Detour.OnDetour -= RegisterDetour;
            Detour.OnUndo -= UnregisterDetour;
            NativeDetour.OnDetour -= RegisterNativeDetour;
            NativeDetour.OnUndo -= UnregisterDetour;
        }

        public void Unload(Assembly asm) {
            if (asm == null || Ignored.Contains(asm))
                return;

            // Unload any HookGen hooks after unloading the mod.
            HookEndpointManager.RemoveAllOwnedBy(asm);
            if (OwnedDetourLists.TryGetValue(asm, out List<IDetour> list)) {
                foreach (IDetour detour in list.ToArray())
                    detour.Dispose();

                if (list.Count > 0)
                    throw new Exception("Some detours failed to unregister in " + asm.FullName);

                OwnedDetourLists.Remove(asm);
            }
        }

        private static readonly string[] HookTypeNames = {
            "MonoMod.RuntimeDetour.NativeDetour",
            "MonoMod.RuntimeDetour.Detour",
            "MonoMod.RuntimeDetour.Hook",
            "MonoMod.RuntimeDetour.ILHook",
        };
        internal Assembly GetHookOwner(StackTrace stack = null) {
            // Stack walking is not fast, but it's the only option
            if (stack == null)
                stack = new StackTrace();

            Assembly owner = null;
            int frameCount = stack.FrameCount;
            string rootDetourTypeName = null;
            for (int i = 0; i < frameCount; i++) {
                StackFrame frame = stack.GetFrame(i);
                MethodBase caller = frame.GetMethod();
                if (caller?.DeclaringType == null)
                    continue;

                string currentCallerTypeName = caller.DeclaringType.FullName;
                if (rootDetourTypeName == null) {
                    // Skip until we've reached a method in Detour/Hook/NativeDetour.
                    if (!HookTypeNames.Contains(currentCallerTypeName))
                        continue;

                    rootDetourTypeName = caller.DeclaringType.FullName;
                    continue;
                }

                // find the invoker of the Detour/Hook/NativeDetour
                if (currentCallerTypeName == rootDetourTypeName)
                    continue;
                
                owner = caller?.DeclaringType.Assembly;
                break;
            }
            if (Ignored.Contains(owner))
                return null;

            return owner;
        }

        internal void TrackDetour(Assembly owner, IDetour detour) {
            if (!OwnedDetourLists.TryGetValue(owner, out List<IDetour> list))
                OwnedDetourLists[owner] = list = new List<IDetour>();

            list.Add(detour);
            DetourOwners[detour] = owner;
        }

        internal bool RegisterILHook(ILHook _detour, MethodBase from, ILContext.Manipulator manipulator) {
            Assembly owner = GetHookOwner();
            if (owner == null)
                return true; // continue with default detour creation, we just don't track it

            OnILHook?.Invoke(owner, from, manipulator);
            TrackDetour(owner, _detour);
            return true;
        }

        internal bool RegisterHook(Hook _detour, MethodBase from, MethodBase to, object target) {
            Assembly owner = GetHookOwner();
            if (owner == null)
                return true; // continue with default detour creation, we just don't track it

            OnHook?.Invoke(owner, from, to, target);
            TrackDetour(owner, _detour);
            return true;
        }

        internal bool RegisterDetour(Detour _detour, MethodBase from, MethodBase to) {
            Assembly owner = GetHookOwner();
            if (owner == null)
                return true; // continue with default detour creation, we just don't track it

            OnDetour?.Invoke(owner, from, to);
            TrackDetour(owner, _detour);
            return true;
        }

        internal bool RegisterNativeDetour(NativeDetour _detour, MethodBase method, IntPtr from, IntPtr to) {
            Assembly owner = GetHookOwner();
            if (owner == null)
                return true; // continue with default detour creation, we just don't track it

            OnNativeDetour?.Invoke(owner, method, from, to);
            TrackDetour(owner, _detour);
            return true;
        }

        internal bool UnregisterDetour(IDetour _detour) {
            if (DetourOwners.TryGetValue(_detour, out Assembly owner)) {
                DetourOwners.Remove(_detour);
                OwnedDetourLists[owner].Remove(_detour);
            }
            return true;
        }
    }
}
