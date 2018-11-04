#if !NETSTANDARD1_X
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.HookGen;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour {
    public sealed class DetourModManager : IDisposable {

        private readonly Dictionary<Assembly, List<IDetour>> OwnedDetourLists = new Dictionary<Assembly, List<IDetour>>();

        public HashSet<Assembly> Ignored = new HashSet<Assembly>();

        public DetourModManager() {
            Ignored.Add(typeof(DetourModManager).GetTypeInfo().Assembly);

            // Keep track of all NativeDetours, Detours and (indirectly) Hooks.
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
                OwnedDetourLists.Remove(asm);
                foreach (IDetour detour in list)
                    detour.Dispose();
            }
        }

        internal List<IDetour> GetOwnedDetourList(StackTrace stack = null, bool add = true) {
            // I deserve to be murdered for this.
            if (stack == null)
                stack = new StackTrace();
            Assembly owner = null;
            int frameCount = stack.FrameCount;
            int state = 0;
            for (int i = 0; i < frameCount; i++) {
                StackFrame frame = stack.GetFrame(i);
                MethodBase caller = frame.GetMethod();
                if (caller?.DeclaringType == null)
                    continue;
                switch (state) {
                    // Skip until we've reached a method in Detour or Hook.
                    case 0:
                        if (caller.DeclaringType.FullName != "MonoMod.RuntimeDetour.NativeDetour" &&
                            caller.DeclaringType.FullName != "MonoMod.RuntimeDetour.Detour" &&
                            caller.DeclaringType.FullName != "MonoMod.RuntimeDetour.Hook") {
                            continue;
                        }
                        state++;
                        continue;

                    // Skip until we're out of Detour and / or Hook.
                    case 1:
                        if (caller.DeclaringType.FullName == "MonoMod.RuntimeDetour.NativeDetour" ||
                            caller.DeclaringType.FullName == "MonoMod.RuntimeDetour.Detour" ||
                            caller.DeclaringType.FullName == "MonoMod.RuntimeDetour.Hook") {
                            continue;
                        }
                        owner = caller?.DeclaringType.GetTypeInfo().Assembly;
                        break;
                }
                break;
            }

            if (owner == null)
                return null;

            if (!OwnedDetourLists.TryGetValue(owner, out List<IDetour> list) && add)
                OwnedDetourLists[owner] = list = new List<IDetour>();
            return list;
        }

        internal bool RegisterDetour(object _detour, MethodBase from, MethodBase to) {
            GetOwnedDetourList()?.Add(_detour as IDetour);
            return true;
        }

        internal bool RegisterNativeDetour(object _detour, MethodBase method, IntPtr from, IntPtr to) {
            StackTrace stack = new StackTrace();

            // Don't register NativeDetours created by higher level Detours.
            int frameCount = stack.FrameCount;
            for (int i = 0; i < frameCount; i++) {
                StackFrame frame = stack.GetFrame(i);
                MethodBase caller = frame.GetMethod();
                if (caller?.DeclaringType == null)
                    continue;
                if (caller.DeclaringType.FullName.StartsWith("MonoMod.RuntimeDetour.") &&
                    caller.DeclaringType.FullName != "MonoMod.RuntimeDetour.NativeDetour")
                    return true;
            }

            GetOwnedDetourList(stack: stack)?.Add(_detour as IDetour);
            return true;
        }

        internal bool UnregisterDetour(object _detour) {
            GetOwnedDetourList(add: false)?.Remove(_detour as IDetour);
            return true;
        }

    }
}
#endif
