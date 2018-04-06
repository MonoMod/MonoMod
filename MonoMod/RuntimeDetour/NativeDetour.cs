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
    public struct NativeDetourData {
        /// <summary>
        /// The method to detour from. Set when the structure is created by the IDetourNativePlatform.
        /// </summary>
        public IntPtr Method;
        /// <summary>
        /// The target method to be called instead. Set when the structure is created by the IDetourNativePlatform.
        /// </summary>
        public IntPtr Target;

        /// <summary>
        /// The size of the detour. Calculated when the structure is created by the IDetourNativePlatform.
        /// </summary>
        public int Size;

        /// <summary>
        /// Platform-specific data.
        /// </summary>
        public IntPtr Extra;
    }

    public class NativeDetour {

        public NativeDetourData Data;
        public IntPtr Backup;

        private bool IsFree;

        public NativeDetour(IntPtr from, IntPtr to) {
            Data = DetourManager.Native.Create(from, to);
            Apply();
        }

        public NativeDetour(MethodBase from, IntPtr to)
            : this(from.GetMethodStart(), to) {
        }
        public NativeDetour(IntPtr from, MethodBase to)
            : this(from, to.GetMethodStart()) {
        }
        public NativeDetour(MethodBase from, MethodBase to)
            : this(from.GetMethodStart(), to.GetMethodStart()) {
        }

        public NativeDetour(Expression<Action> from, IntPtr to)
            : this(from.Body.GetMethodStart(), to) {
        }
        public NativeDetour(IntPtr from, Expression<Action> to)
            : this(from, to.Body.GetMethodStart()) {
        }
        public NativeDetour(Expression<Action> from, Expression<Action> to)
            : this(from.Body.GetMethodStart(), to.Body.GetMethodStart()) {
        }

        public NativeDetour(Delegate from, IntPtr to)
            : this(from.Method.GetMethodStart(), to) {
        }
        public NativeDetour(IntPtr from, Delegate to)
            : this(from, to.Method.GetMethodStart()) {
        }
        public NativeDetour(Delegate from, Delegate to)
            : this(from.Method.GetMethodStart(), to.Method.GetMethodStart()) {
        }

        /// <summary>
        /// Apply the native detour, creating a backup. This automatically happens when creating the RawDetour.
        /// </summary>
        public void Apply() {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            if (Backup == IntPtr.Zero) {
                Backup = DetourManager.Native.MemAlloc(Data.Size);
                DetourManager.Native.Copy(Data.Method, Backup, Data.Size);
            }

            DetourManager.Native.Apply(Data);
        }

        /// <summary>
        /// Undo the native detour. Doesn't free the detour native data, allowing you to reapply it later.
        /// </summary>
        public void Undo() {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            DetourManager.Native.Copy(Backup, Data.Method, Data.Size);
        }

        /// <summary>
        /// Free the detour's data without undoing it. This makes any further operations on this Detour invalid.
        /// </summary>
        public void Free() {
            if (IsFree)
                return;
            IsFree = true;
        }

    }

    public class NativeDetour<T> : NativeDetour {
        public NativeDetour(Expression<Func<T>> from, IntPtr to)
            : base(from.Body.GetMethodStart(), to) {
        }
        public NativeDetour(IntPtr from, Expression<Func<T>> to)
            : base(from, to.Body.GetMethodStart()) {
        }
        public NativeDetour(Expression<Func<T>> from, Expression<Func<T>> to)
            : base(from.Body.GetMethodStart(), to.Body.GetMethodStart()) {
        }

        public NativeDetour(T from, IntPtr to)
            : base((from as Delegate).GetMethodStart(), to) {
        }
        public NativeDetour(IntPtr from, T to)
            : base(from, (to as Delegate).GetMethodStart()) {
        }
        public NativeDetour(T from, T to)
            : base((from as Delegate).GetMethodStart(), (to as Delegate).Method.GetMethodStart()) {
        }
    }
}
