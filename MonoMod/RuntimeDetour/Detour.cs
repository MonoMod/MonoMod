using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;
using System.Linq.Expressions;

namespace MonoMod.RuntimeDetour {
    /// <summary>
    /// A fully managed detour.
    /// Multiple Detours for a method to detour from can exist at any given time. Detours can be layered.
    /// If you're writing your own detour manager or need to detour native functions, it's better to create instances of NativeDetour instead.
    /// </summary>
    public class Detour : IDetour {

        private static Dictionary<MethodBase, List<Detour>> _DetourMap = new Dictionary<MethodBase, List<Detour>>();

        public bool IsValid => _DetourMap[Method].Contains(this);

        public int Index {
            get {
                return _DetourMap[Method].IndexOf(this);
            }
            set {
                List<Detour> detours = _DetourMap[Method];

                lock (detours) {
                    int valueOld = detours.IndexOf(this);
                    if (valueOld == -1)
                        throw new InvalidOperationException("This detour has been undone.");

                    detours.RemoveAt(valueOld);

                    if (value > valueOld)
                        value--;

                    try {
                        detours.Insert(value, this);
                    } catch {
                        // Too lazy to manually check the bounds.
                        detours.Insert(valueOld, this);
                        throw;
                    }

                    Detour top = detours[detours.Count - 1];
                    if (top != this)
                        TopDetourUndo();
                    top.TopDetourApply();
                }
            }
        }

        public readonly long ID;

        public readonly MethodBase Method;
        public readonly MethodBase Target;

        private NativeDetour TopDetour;

        public Detour(MethodBase from, MethodBase to) {
            ID = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
            Method = from;
            Target = to;

            // Add the detour to the detour map.
            List<Detour> detours;
            lock (_DetourMap) {
                if (!_DetourMap.TryGetValue(Method, out detours))
                    _DetourMap[Method] = detours = new List<Detour>();
            }
            lock (detours) {
                if (detours.Count > 0)
                    detours[detours.Count - 1].TopDetourUndo();
                TopDetourApply();
                detours.Add(this);
            }
        }

        public Detour(MethodBase method, IntPtr to)
            : this(method, DetourManager.GenerateNativeProxy(to, method)) {
        }

        public Detour(Delegate from, IntPtr to)
            : this(from.Method, to) {
        }
        public Detour(Delegate from, Delegate to)
            : this(from.Method, to.Method) {
        }

        public Detour(Expression from, IntPtr to)
            : this(((MethodCallExpression) from).Method, to) {
        }
        public Detour(Expression from, Expression to)
            : this(((MethodCallExpression) from).Method, ((MethodCallExpression) to).Method) {
        }

        public Detour(Expression<Action> from, IntPtr to)
            : this(from.Body, to) {
        }
        public Detour(Expression<Action> from, Expression<Action> to)
            : this(from.Body, to.Body) {
        }

        /// <summary>
        /// This is a no-op on fully managed detours.
        /// </summary>
        public void Apply() {
            if (!IsValid)
                throw new InvalidOperationException("This detour has been undone.");

            // no-op.
        }

        /// <summary>
        /// Permanently undo the detour, while also freeing any related unmanaged resources. This makes any further operations on this detour invalid.
        /// </summary>
        public void Undo() {
            if (!IsValid)
                throw new InvalidOperationException("This detour has been undone.");

            List<Detour> detours = _DetourMap[Method];
            detours.Remove(this);
            TopDetourUndo();
            if (detours.Count > 0)
                detours[detours.Count - 1].TopDetourApply();
        }

        /// <summary>
        /// Free the detour, while also permanently undoing it. This makes any further operations on this detour invalid.
        /// </summary>
        public void Free() {
            // NativeDetour allows freeing without undoing, but Detours are fully managed.
            // Freeing a Detour without undoing it would leave a hole open in the detour chain.
            Undo();
        }

        public DynamicMethod GenerateTrampoline(MethodBase signature = null) {
            throw new NotImplementedException();
        }

        public T GenerateTrampoline<T>() where T : class {
            throw new NotImplementedException();
        }

        private void TopDetourUndo() {
            if (TopDetour == null)
                return;

            TopDetour.Undo();
            TopDetour.Free();
            TopDetour = null;
        }
        private void TopDetourApply() {
            if (TopDetour != null)
                return;

            TopDetour = new NativeDetour(Method, Target);
        }
    }

    public class Detour<T> : Detour  {
        public Detour(Expression<Func<T>> from, IntPtr to)
            : base(from.Body, to) {
        }
        public Detour(Expression<Func<T>> from, Expression<Func<T>> to)
            : base(from.Body, to.Body) {
        }

        public Detour(T from, IntPtr to)
            : base(from as Delegate, to) {
        }
        public Detour(T from, T to)
            : base(from as Delegate, to as Delegate) {
        }
    }
}
