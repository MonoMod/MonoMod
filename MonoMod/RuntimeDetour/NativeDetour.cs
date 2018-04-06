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

        public readonly NativeDetourData Data;
        public readonly MethodBase Method;

        private DynamicMethod BackupMethod;
        private IntPtr BackupNative;

        private bool IsFree;

        public NativeDetour(MethodBase method, IntPtr from, IntPtr to) {
            Data = DetourManager.Native.Create(from, to);
            Method = method;

            if (Method != null && Method.GetMethodBody() != null)
                BackupMethod = method.CreateILCopy();

            Apply();
        }

        public NativeDetour(IntPtr from, IntPtr to)
            : this(null, from, to) {
        }
        public NativeDetour(MethodBase from, IntPtr to)
            : this(from, from.GetJITStart(), to) {
        }

        public NativeDetour(IntPtr from, MethodBase to)
            : this(from, to.GetJITStart()) {
        }
        public NativeDetour(MethodBase from, MethodBase to)
            : this(from, to.GetJITStart()) {
        }

        public NativeDetour(Delegate from, IntPtr to)
            : this(from.Method, to) {
        }
        public NativeDetour(IntPtr from, Delegate to)
            : this(from, to.Method) {
        }
        public NativeDetour(Delegate from, Delegate to)
            : this(from.Method, to.Method) {
        }

        public NativeDetour(Expression from, IntPtr to)
            : this(((MethodCallExpression) from).Method, to) {
        }
        public NativeDetour(IntPtr from, Expression to)
            : this(from, ((MethodCallExpression) to).Method) {
        }
        public NativeDetour(Expression from, Expression to)
            : this(((MethodCallExpression) from).Method, ((MethodCallExpression) to).Method) {
        }

        public NativeDetour(Expression<Action> from, IntPtr to)
            : this(from.Body, to) {
        }
        public NativeDetour(IntPtr from, Expression<Action> to)
            : this(from, to.Body) {
        }
        public NativeDetour(Expression<Action> from, Expression<Action> to)
            : this(from.Body, to.Body) {
        }

        /// <summary>
        /// Apply the native detour, creating a backup. This automatically happens when creating the RawDetour.
        /// </summary>
        public void Apply() {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            if (BackupMethod == null && BackupNative == IntPtr.Zero) {
                BackupNative = DetourManager.Native.MemAlloc(Data.Size);
                DetourManager.Native.Copy(Data.Method, BackupNative, Data.Size);
            }

            DetourManager.Native.Apply(Data);
        }

        /// <summary>
        /// Undo the native detour. Doesn't free the detour native data, allowing you to reapply it later.
        /// </summary>
        public void Undo() {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            DetourManager.Native.Copy(BackupNative, Data.Method, Data.Size);
        }

        /// <summary>
        /// Free the detour's data without undoing it. This makes any further operations on this Detour invalid.
        /// </summary>
        public void Free() {
            if (IsFree)
                return;
            IsFree = true;

            if (BackupNative != IntPtr.Zero) {
                DetourManager.Native.MemFree(BackupNative);
            }
            DetourManager.Native.Free(Data);
        }

        public T GenerateTrampoline<T>() where T : class {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");
            if (!typeof(Delegate).IsAssignableFrom(typeof(T)))
                throw new InvalidOperationException($"Type {typeof(T)} not a delegate type.");

            if (BackupMethod != null) {
                // If we're detouring an IL method and have an IL copy, invoke the IL copy.
                return BackupMethod.CreateDelegate(typeof(T)) as T;
            }

            // Otherwise, undo the detour, call the method and reapply the detour.

            MethodInfo delegateInvoke = typeof(T).GetMethod("Invoke");

            Type returnType = delegateInvoke?.ReturnType;

            ParameterInfo[] args = delegateInvoke.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            DynamicMethod dm = new DynamicMethod(
                $"trampoline_native_{Method.Name}_{GetHashCode()}",
                returnType, argTypes,
                Method.Module, true
            );
            ILGenerator il = dm.GetILGenerator();

            il.EmitDetourCopy(BackupNative, Data.Method, Data.Size);

            // TODO: [RuntimeDetour] Use specialized Ldarg.* if possible; What about ref types?
            for (int i = 0; i < argTypes.Length; i++)
                il.Emit(OpCodes.Ldarg, i);

            // TODO: Wrap call in try, reapply the detour in finalize.

            if (Method is MethodInfo)
                il.Emit(OpCodes.Call, (MethodInfo) Method);
            else if (Method is ConstructorInfo)
                il.Emit(OpCodes.Call, (ConstructorInfo) Method);

            il.EmitDetourApply(Data);

            il.Emit(OpCodes.Ret);

            return dm.CreateDelegate(typeof(T)) as T;
        }

    }

    public class NativeDetour<T> : NativeDetour  {
        public NativeDetour(Expression<Func<T>> from, IntPtr to)
            : base(from.Body, to) {
        }
        public NativeDetour(IntPtr from, Expression<Func<T>> to)
            : base(from, to.Body) {
        }
        public NativeDetour(Expression<Func<T>> from, Expression<Func<T>> to)
            : base(from.Body, to.Body) {
        }

        public NativeDetour(T from, IntPtr to)
            : base(from as Delegate, to) {
        }
        public NativeDetour(IntPtr from, T to)
            : base(from, to as Delegate) {
        }
        public NativeDetour(T from, T to)
            : base(from as Delegate, to as Delegate) {
        }

    }
}
