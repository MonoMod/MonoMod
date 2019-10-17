using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Mono.Cecil;
using System.Collections.ObjectModel;

namespace MonoMod.RuntimeDetour {
    public struct ILHookConfig {
        public bool ManualApply;
        public int Priority;
        public string ID;
        public IEnumerable<string> Before;
        public IEnumerable<string> After;
    }

    public class ILHook : ISortableDetour {

        // "Detour" is the wrong term, but it's consistent with Hook.OnDetour and Detour.OnDetour
        // TODO: Consider breaking backwards compatibility in the future.
        public static Func<ILHook, MethodBase, ILContext.Manipulator, bool> OnDetour;
        public static Func<ILHook, bool> OnUndo;

        private static DetourConfig ILDetourConfig = new DetourConfig() {
            Priority = int.MinValue / 8,
            Before = new string[] { "*" }
        };

        private static Dictionary<MethodBase, Context> _Map = new Dictionary<MethodBase, Context>();
        private static uint _GlobalIndexNext = uint.MinValue;

        private Context _Ctx => _Map.TryGetValue(Method, out Context ctx) ? ctx : null;

        public bool IsValid => Index != -1;
        public bool IsApplied { get; private set; }

        public int Index => _Ctx?.Chain.IndexOf(this) ?? -1;
        public int MaxIndex => _Ctx?.Chain.Count ?? -1;

        private readonly uint _GlobalIndex;
        public uint GlobalIndex => _GlobalIndex;

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

        private string _ID;
        public string ID {
            get => _ID;
            set {
                if (string.IsNullOrEmpty(value))
                    value = Manipulator.Method?.GetID(simple: true) ?? GetHashCode().ToString();
                if (_ID == value)
                    return;
                _ID = value;
                _Ctx.Refresh();
            }
        }

        private List<string> _Before = new List<string>();
        private ReadOnlyCollection<string> _BeforeRO;
        public IEnumerable<string> Before {
            get => _BeforeRO ?? (_BeforeRO = _Before.AsReadOnly());
            set {
                lock (_Before) {
                    _Before.Clear();
                    if (value != null)
                        foreach (string id in value)
                            _Before.Add(id);
                    _Ctx.Refresh();
                }
            }
        }

        private List<string> _After = new List<string>();
        private ReadOnlyCollection<string> _AfterRO;
        public IEnumerable<string> After {
            get => _AfterRO ?? (_AfterRO = _After.AsReadOnly());
            set {
                lock (_After) {
                    _After.Clear();
                    if (value != null)
                        foreach (string id in value)
                            _After.Add(id);
                    _Ctx.Refresh();
                }
            }
        }

        public readonly MethodBase Method;
        public readonly ILContext.Manipulator Manipulator;

        public ILHook(MethodBase from, ILContext.Manipulator manipulator, ref ILHookConfig config) {
            Method = from.Pin();
            Manipulator = manipulator;

            _GlobalIndex = _GlobalIndexNext++;

            _Priority = config.Priority;
            _ID = config.ID;
            if (config.Before != null)
                foreach (string id in config.Before)
                    _Before.Add(id);
            if (config.After != null)
                foreach (string id in config.After)
                    _After.Add(id);

            // Add the hook to the hook map.
            Context ctx;
            lock (_Map) {
                if (!_Map.TryGetValue(Method, out ctx))
                    _Map[Method] = ctx = new Context(Method);
            }
            lock (ctx) {
                ctx.Add(this);
                // The chain gets refreshed when the hook is applied.
            }

            if (!config.ManualApply)
                Apply();
        }
        public ILHook(MethodBase from, ILContext.Manipulator manipulator, ILHookConfig config)
            : this(from, manipulator, ref config) {
        }
        public ILHook(MethodBase from, ILContext.Manipulator manipulator)
            : this(from, manipulator, DetourContext.Current?.ILHookConfig ?? default) {
        }

        public void Apply() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(ILHook));

            if (IsApplied)
                return;

            if (!(OnDetour?.InvokeWhileTrue(this, Method, Manipulator) ?? true))
                return;

            IsApplied = true;
            _Ctx.Refresh();
        }

        public void Undo() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(ILHook));

            if (!IsApplied)
                return;

            if (!(OnUndo?.InvokeWhileTrue(this) ?? true))
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

                    bool hasApplied = false;
                    foreach (ILHook cb in hooks) {
                        if (cb.IsApplied) {
                            hasApplied = true;
                            break;
                        }
                    }

                    if (!hasApplied)
                        return;

                    DetourSorter<ILHook>.Sort(hooks);

                    MethodBase dm;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(Method)) {
                        MethodDefinition def = dmd.Definition;
                        foreach (ILHook cb in hooks)
                            if (cb.IsApplied)
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
    }
}
