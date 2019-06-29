using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Mono.Cecil;

namespace MonoMod.RuntimeDetour {
    public struct ILHookConfig {
        public bool ManualApply;
        public int Priority;
    }

    public class ILHook : IDetour {

        private static DetourConfig ILDetourConfig = new DetourConfig() {
            Priority = int.MinValue / 8
        };

        private static Dictionary<MethodBase, Context> _Map = new Dictionary<MethodBase, Context>();
        private static int _GlobalIndexNext = int.MinValue;

        private Context _Ctx => _Map.TryGetValue(Method, out Context ctx) ? ctx : null;

        public static event Func<AssemblyName, ModuleDefinition> OnGenerateCecilModule;
        public static ModuleDefinition GenerateCecilModule(AssemblyName name)
            => OnGenerateCecilModule?.InvokeWhileNull<ModuleDefinition>(name);

        public bool IsValid => Index != -1;
        public bool IsApplied { get; private set; }

        public int Index => _Ctx?.Chain.IndexOf(this) ?? -1;
        public int MaxIndex => _Ctx?.Chain.Count ?? -1;

        private int _Priority;
        public int Priority {
            get => _Priority;
            set {
                if (_Priority == value)
                    return;
                _Priority = value;
                _Ctx.Refresh();
            }
        }
        private int _GlobalIndex;
        public readonly MethodBase Method;
        public readonly ILContext.Manipulator Manipulator;

        public ILHook(MethodBase from, ILContext.Manipulator manipulator, ref ILHookConfig config) {
            Method = from.Pin();
            Manipulator = manipulator;

            _Priority = config.Priority;
            _GlobalIndex = _GlobalIndexNext++;

            // Add the hook to the hook map.
            Context ctx;
            lock (_Map) {
                if (!_Map.TryGetValue(Method, out ctx))
                    _Map[Method] = ctx = new Context(Method);
            }
            _Ctx.Add(this);
            lock (ctx) {
                ctx.Chain.Add(this);
                // The chain gets refreshed when the hook is applied.
            }

            if (!config.ManualApply)
                Apply();
        }
        public ILHook(MethodBase from, ILContext.Manipulator manipulator, ILHookConfig config)
            : this(from, manipulator, ref config) {
        }
        public ILHook(MethodBase from, ILContext.Manipulator manipulator)
            : this(from, manipulator, default) {
        }

        public void Apply() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(ILHook));

            if (IsApplied)
                return;
            IsApplied = true;
            _Ctx.Refresh();
        }

        public void Undo() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(ILHook));

            if (!IsApplied)
                return;
            IsApplied = false;
            _Ctx.Refresh();
        }

        public void Free() {
            // NativeDetour allows freeing without undoing, but Detours are fully managed.
            // Freeing a Detour without undoing it would leave a hole open in the detour chain.
            if (!IsValid)
                return;

            Undo();

            _Ctx.Remove(this);

            Method.Unpin();
        }

        public void Dispose() {
            if (!IsValid)
                return;

            Undo();
            Free();
        }

        public MethodBase GenerateTrampoline(MethodBase signature = null) {
            throw new NotSupportedException();
        }

        public T GenerateTrampoline<T>() where T : Delegate {
            throw new NotSupportedException();
        }

        private class Context {
            public List<ILHook> Chain = new List<ILHook>();
            public HashSet<ILContext> Active = new HashSet<ILContext>();
            public MethodBase Method;
            public Detour Detour;

            public Context(MethodBase method) {
                Method = method;
            }

            public void Add(ILHook hook) {
                List<ILHook> hooks = Chain;
                lock (hooks) {
                    hooks.Add(hook);
                }
            }

            public void Remove(ILHook hook) {
                List<ILHook> hooks = Chain;
                lock (hooks) {
                    hooks.Remove(hook);
                    if (hooks.Count == 0) {
                        Refresh();
                        lock (_Map) {
                            _Map.Remove(Method);
                        }
                    }
                }
            }

            public void Refresh() {
                List<ILHook> hooks = Chain;
                lock (hooks) {
                    foreach (ILContext il in Active)
                        il.Dispose();
                    Active.Clear();

                    Detour?.Dispose();
                    Detour = null;

                    if (hooks.Count == 0)
                        return;

                    hooks.Sort(PriorityComparer.Instance);

                    MethodBase dm;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(Method, GenerateCecilModule)) {
                        MethodDefinition def = dmd.Definition;
                        foreach (ILHook cb in hooks)
                            InvokeManipulator(def, cb.Manipulator);

                        dm = dmd.Generate();
                    }

                    Detour = new Detour(Method, dm, ref ILDetourConfig);
                }
            }

            private void InvokeManipulator(MethodDefinition def, ILContext.Manipulator cb) {
                ILContext il = new ILContext(def);
                il.ReferenceBag = RuntimeILReferenceBag.Instance;
                il.Invoke(cb);
                if (il.IsReadOnly) {
                    il.Dispose();
                    return;
                }

                // Free the now useless MethodDefinition and ILProcessor references.
                // This also prevents clueless people from storing the ILContext elsewhere
                // and reusing it outside of the IL manipulation context.
                il.MakeReadOnly();
                Active.Add(il);
                return;
            }
        }

        private sealed class PriorityComparer : IComparer<ILHook> {
            public static readonly PriorityComparer Instance = new PriorityComparer();
            public int Compare(ILHook a, ILHook b) {
                int delta = a._Priority - b._Priority;
                if (delta == 0)
                    delta = a._GlobalIndex - b._GlobalIndex;
                return delta;
            }
        }
    }
}
