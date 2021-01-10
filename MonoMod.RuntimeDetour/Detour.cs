using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using Mono.Cecil.Cil;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
#if !NET35
using System.Collections.Concurrent;
#endif

namespace MonoMod.RuntimeDetour {
    public struct DetourConfig {
        public bool ManualApply;
        public int Priority;
        public string ID;
        public IEnumerable<string> Before;
        public IEnumerable<string> After;
    }

    /// <summary>
    /// A fully managed detour.
    /// Multiple Detours for a method to detour from can exist at any given time. Detours can be layered.
    /// If you're writing your own detour manager or need to detour native functions, it's better to create instances of NativeDetour instead.
    /// </summary>
    public class Detour : ISortableDetour {
#if NET35
        private static Dictionary<MethodBase, List<Detour>> _DetourMap = new Dictionary<MethodBase, List<Detour>>(new GenericMethodInstantiationComparer());
#else
        private static ConcurrentDictionary<MethodBase, List<Detour>> _DetourMap = new ConcurrentDictionary<MethodBase, List<Detour>>(new GenericMethodInstantiationComparer());
#endif
        private static Dictionary<MethodBase, MethodInfo> _BackupMethods = new Dictionary<MethodBase, MethodInfo>();
        private static uint _GlobalIndexNext = uint.MinValue;

        private List<Detour> _DetourChain => _DetourMap.TryGetValue(Method, out List<Detour> detours) ? detours : null;

        public static Func<Detour, MethodBase, MethodBase, bool> OnDetour;
        public static Func<Detour, bool> OnUndo;
        public static Func<Detour, MethodBase, MethodBase> OnGenerateTrampoline;

        public bool IsValid => Index != -1;
        public bool IsApplied { get; private set; }
        private bool IsTop => _TopDetour != null;

        public int Index => _DetourChain?.IndexOf(this) ?? -1;
        public int MaxIndex => _DetourChain?.Count ?? -1;

        private readonly uint _GlobalIndex;
        public uint GlobalIndex => _GlobalIndex;

        private int _Priority;
        public int Priority {
            get => _Priority;
            set {
                if (_Priority == value)
                    return;
                _Priority = value;
                _RefreshChain(Method);
            }
        }

        private string _ID;
        public string ID {
            get => _ID;
            set {
                if (string.IsNullOrEmpty(value))
                    value = Target.GetID(simple: true);
                if (_ID == value)
                    return;
                _ID = value;
                _RefreshChain(Method);
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
                    _RefreshChain(Method);
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
                    _RefreshChain(Method);
                }
            }
        }

        public readonly MethodBase Method;
        public readonly MethodBase Target;
        public readonly MethodBase TargetReal;

        // The active NativeDetour. Only present if the current Detour is on the top of the Detour chain.
        private NativeDetour _TopDetour;

        // Called by the generated trampolines, updated when the Detour chain changes.
        private MethodInfo _ChainedTrampoline;

        public Detour(MethodBase from, MethodBase to, ref DetourConfig config) {
            if (from.Equals(to))
                throw new ArgumentException("Cannot detour a method to itself!");

            MMDbgLog.Log($"detour from {from.GetID()} to {to.GetID()}");

            Method = from/*.Pin()*/;
            Target = to.Pin(); // Only pin once, unpin on Free!
            TargetReal = DetourHelper.Runtime.GetDetourTarget(from, to)/*.Pin()*/;

            _GlobalIndex = _GlobalIndexNext++;

            _Priority = config.Priority;
            _ID = config.ID;
            if (config.Before != null)
                foreach (string id in config.Before)
                    _Before.Add(id);
            if (config.After != null)
                foreach (string id in config.After)
                    _After.Add(id);

            lock (_BackupMethods) {
                if ((!_BackupMethods.TryGetValue(Method, out MethodInfo backup) || backup == null) &&
                    (backup = Method.CreateILCopy()) != null)
                    _BackupMethods[Method] = backup.Pin(); // Only pin once, unpin on Free!
            }

            // Generate a "chained trampoline" DynamicMethod.
            ParameterInfo[] args = Method.GetParameters();
            Type[] argTypes;
            if (!Method.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = Method.GetThisParamType();
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"Chain<{Method.GetID(simple: true)}>?{GetHashCode()}",
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes
            ))
                _ChainedTrampoline = dmd.StubCriticalDetour().Generate().Pin();

            // Add the detour to the detour map.
            List<Detour> detours;
#if NET35
            lock (_DetourMap) {
                if (!_DetourMap.TryGetValue(Method, out detours))
                    _DetourMap[Method] = detours = new List<Detour>();
            }
#else
            detours = _DetourMap.GetOrAdd(Method, m => new List<Detour>());
#endif
            lock (detours) {
                detours.Add(this);
                // The chain gets refreshed when the detour is applied.
            }

            if (!config.ManualApply)
                Apply();
        }
        public Detour(MethodBase from, MethodBase to, DetourConfig config)
            : this(from, to, ref config) {
        }
        public Detour(MethodBase from, MethodBase to)
            : this(from, to, DetourContext.Current?.DetourConfig ?? default) {
        }

        public Detour(MethodBase method, IntPtr to, ref DetourConfig config)
            : this(method, DetourHelper.GenerateNativeProxy(to, method), ref config) {
        }
        public Detour(MethodBase method, IntPtr to, DetourConfig config)
            : this(method, DetourHelper.GenerateNativeProxy(to, method), ref config) {
        }
        public Detour(MethodBase method, IntPtr to)
            : this(method, DetourHelper.GenerateNativeProxy(to, method)) {
        }

        public Detour(Delegate from, IntPtr to, ref DetourConfig config)
            : this(from.Method, to, ref config) {
        }
        public Detour(Delegate from, IntPtr to, DetourConfig config)
            : this(from.Method, to, ref config) {
        }
        public Detour(Delegate from, IntPtr to)
            : this(from.Method, to) {
        }
        public Detour(Delegate from, Delegate to, ref DetourConfig config)
            : this(from.Method, to.Method, ref config) {
        }
        public Detour(Delegate from, Delegate to, DetourConfig config)
            : this(from.Method, to.Method, ref config) {
        }
        public Detour(Delegate from, Delegate to)
            : this(from.Method, to.Method) {
        }

        public Detour(Expression from, IntPtr to, ref DetourConfig config)
            : this(((MethodCallExpression) from).Method, to, ref config) {
        }
        public Detour(Expression from, IntPtr to, DetourConfig config)
            : this(((MethodCallExpression) from).Method, to, ref config) {
        }
        public Detour(Expression from, IntPtr to)
            : this(((MethodCallExpression) from).Method, to) {
        }
        public Detour(Expression from, Expression to, ref DetourConfig config)
            : this(((MethodCallExpression) from).Method, ((MethodCallExpression) to).Method, ref config) {
        }
        public Detour(Expression from, Expression to, DetourConfig config)
            : this(((MethodCallExpression) from).Method, ((MethodCallExpression) to).Method, ref config) {
        }
        public Detour(Expression from, Expression to)
            : this(((MethodCallExpression) from).Method, ((MethodCallExpression) to).Method) {
        }

        public Detour(Expression<Action> from, IntPtr to, ref DetourConfig config)
            : this(from.Body, to, ref config) {
        }
        public Detour(Expression<Action> from, IntPtr to, DetourConfig config)
            : this(from.Body, to, ref config) {
        }
        public Detour(Expression<Action> from, IntPtr to)
            : this(from.Body, to) {
        }
        public Detour(Expression<Action> from, Expression<Action> to, ref DetourConfig config)
            : this(from.Body, to.Body, ref config) {
        }
        public Detour(Expression<Action> from, Expression<Action> to, DetourConfig config)
            : this(from.Body, to.Body, ref config) {
        }
        public Detour(Expression<Action> from, Expression<Action> to)
            : this(from.Body, to.Body) {
        }

        /// <summary>
        /// Mark the detour as applied in the detour chain. This can be done automatically when creating an instance.
        /// </summary>
        public void Apply() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(Detour));

            if (IsApplied)
                return;

            if (!(OnDetour?.InvokeWhileTrue(this, Method, Target) ?? true))
                return;

            IsApplied = true;
            _RefreshChain(Method);
        }

        /// <summary>
        /// Undo the detour without freeing it, allowing you to reapply it later.
        /// </summary>
        public void Undo() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(Detour));

            if (!IsApplied)
                return;

            if (!(OnUndo?.InvokeWhileTrue(this) ?? true))
                return;

            IsApplied = false;
            _RefreshChain(Method);
        }

        /// <summary>
        /// Free the detour, while also permanently undoing it. This makes any further operations on this detour invalid.
        /// </summary>
        public void Free() {
            // NativeDetour allows freeing without undoing, but Detours are fully managed.
            // Freeing a Detour without undoing it would leave a hole open in the detour chain.
            if (!IsValid)
                return;

            Undo();

            List<Detour> detours = _DetourChain;
            lock (detours) {
                detours.Remove(this);
                if (detours.Count == 0) {
                    lock (_BackupMethods) {
                        if (_BackupMethods.TryGetValue(Method, out MethodInfo backup)) {
                            backup.Unpin();
                            _BackupMethods.Remove(Method);
                        }
                    }
#if NET35
                    lock (_DetourMap) {
                        _DetourMap.Remove(Method);
                    }
#else
                    _DetourMap.TryRemove(Method, out _);
#endif
                }
            }

            _ChainedTrampoline.Unpin();
            Target.Unpin();
        }

        /// <summary>
        /// Undo and free this temporary detour.
        /// </summary>
        public void Dispose() {
            if (!IsValid)
                return;

            Undo();
            Free();
        }

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
        public MethodBase GenerateTrampoline(MethodBase signature = null) {
            MethodBase remoteTrampoline = OnGenerateTrampoline?.InvokeWhileNull<MethodBase>(this, signature);
            if (remoteTrampoline != null)
                return remoteTrampoline;

            if (signature == null)
                signature = Target;

            // Note: It'd be more performant to skip this step and just return the "chained trampoline."
            // Unfortunately, it'd allow a third party to break the Detour trampoline chain, among other things.
            // Instead, we create and return a DynamicMethod calling the "chained trampoline."

            Type returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);

            ParameterInfo[] args = signature.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"Trampoline<{Method.GetID(simple: true)}>?{GetHashCode()}",
                returnType, argTypes
            )) {
                ILProcessor il = dmd.GetILProcessor();

                for (int i = 0; i < 32; i++) {
                    // Prevent mono from inlining the DynamicMethod.
                    il.Emit(OpCodes.Nop);
                }

                // Jmp and older versions of mono don't work well together.
                // il.Emit(OpCodes.Jmp, _ChainedTrampoline);

                // Manually call the target method instead.
                for (int i = 0; i < argTypes.Length; i++)
                    il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Call, _ChainedTrampoline);
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }
        }

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
        public T GenerateTrampoline<T>() where T : Delegate {
            if (!typeof(Delegate).IsAssignableFrom(typeof(T)))
                throw new InvalidOperationException($"Type {typeof(T)} not a delegate type.");

            return GenerateTrampoline(typeof(T).GetMethod("Invoke")).CreateDelegate(typeof(T)) as T;
        }

        private void _TopUndo() {
            if (_TopDetour == null)
                return;

            _TopDetour.Undo();
            _TopDetour.Free();
            _TopDetour = null;

            Method.Unpin();
            TargetReal.Unpin();
        }

        private void _TopApply() {
            if (_TopDetour != null)
                return;

            // GetNativeStart to avoid repins and managed copies.
            _TopDetour = new NativeDetour(Method.Pin().GetNativeStart(), TargetReal.Pin().GetNativeStart());
        }

        private static int compileMethodSubscribed = 0;
        private static void _OnCompileMethod(MethodBase method, IntPtr codeStart, ulong codeLen) {
            if (method == null)
                return;

            MMDbgLog.Log("compiling: " + method.GetID());
            if (_DetourMap.TryGetValue(method, out List<Detour> detours)) {
                Detour top = detours.FindLast(d => d.IsTop);
                /*top?._TopUndo();
                top?._TopApply();*/
                top?._TopDetour?.ChangeSource(codeStart);
            }
        }

        private static void _RefreshChain(MethodBase method) {
            // ensure we're subscribed to the event before doing anything
            if (Interlocked.CompareExchange(ref compileMethodSubscribed, 1, 0) == 0) {
                DetourHelper.Runtime.OnMethodCompiled += _OnCompileMethod;
            }

            MMDbgLog.Log($"detours applying for {method.GetID()}");

            List<Detour> detours = _DetourMap[method];
            lock (detours) {
                DetourSorter<Detour>.Sort(detours);

                Detour topOld = detours.FindLast(d => d.IsTop);
                Detour topNew = detours.FindLast(d => d.IsApplied);

                if (topOld != topNew)
                    topOld?._TopUndo();

                if (detours.Count == 0)
                    return;

                MethodBase prev = _BackupMethods[method];
                foreach (Detour detour in detours) {
                    if (!detour.IsApplied)
                        continue;

                    MethodBase next = detour._ChainedTrampoline;

                    using (NativeDetour link = new NativeDetour(
                        detour._ChainedTrampoline.GetNativeStart(),
                        prev.GetNativeStart()
                    )) {
                        // This link detour is applied permanently.
                        link.Free(); // Dispose will no longer undo.
                    }

                    prev = detour.Target;
                }

                if (topOld != topNew)
                    topNew?._TopApply();
            }
        }
    }

    public class Detour<T> : Detour where T : Delegate {
        public Detour(T from, IntPtr to, ref DetourConfig config)
            : base(from, to, ref config) {
        }
        public Detour(T from, IntPtr to, DetourConfig config)
            : base(from, to, ref config) {
        }
        public Detour(T from, IntPtr to)
            : base(from, to) {
        }
        public Detour(T from, T to, ref DetourConfig config)
            : base(from, to, ref config) {
        }
        public Detour(T from, T to, DetourConfig config)
            : base(from, to, ref config) {
        }
        public Detour(T from, T to)
            : base(from, to) {
        }
    }
}
