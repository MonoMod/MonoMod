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

        public DynamicMethod GenerateTrampoline(MethodBase signature = null) {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            if (BackupMethod != null) {
                // If we're detouring an IL method and have an IL copy, invoke the IL copy.
                // Note that this ignores the passed signature.
                return BackupMethod;
            }

            if (signature == null)
                signature = BackupMethod;
            if (signature == null) {
                throw new ArgumentNullException("A signature must be given if the NativeDetour doesn't hold a reference to a managed method.");
            }

            // Otherwise, undo the detour, call the method and reapply the detour.

            MethodBase methodCallable = Method;
            if (methodCallable == null) {
                methodCallable = _GenerateNativeProxy(signature);
            }

            Type returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);

            ParameterInfo[] args = signature.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            DynamicMethod dm;
            string name = $"trampoline_native_{Method?.Name.ToString() ?? ((long) Data.Method).ToString("X16")}_{GetHashCode()}";
            if (Method != null) {
                dm = new DynamicMethod(
                    name,
                    returnType, argTypes,
                    Method.DeclaringType,
                    true
                );
            } else {
                dm = new DynamicMethod(
                    name,
                    returnType, argTypes,
                    true
                );
            }
            ILGenerator il = dm.GetILGenerator();

            il.EmitDetourCopy(BackupNative, Data.Method, Data.Size);

            // TODO: Use specialized Ldarg.* if possible; What about ref types?
            for (int i = 0; i < argTypes.Length; i++)
                il.Emit(OpCodes.Ldarg, i);

            // TODO: Wrap call in try, reapply the detour in finalize.

            if (methodCallable is MethodInfo)
                il.Emit(OpCodes.Call, (MethodInfo) methodCallable);
            else if (methodCallable is ConstructorInfo)
                il.Emit(OpCodes.Call, (ConstructorInfo) methodCallable);

            il.EmitDetourApply(Data);

            il.Emit(OpCodes.Ret);

            return dm;
        }

        public T GenerateTrampoline<T>() where T : class {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");
            if (!typeof(Delegate).IsAssignableFrom(typeof(T)))
                throw new InvalidOperationException($"Type {typeof(T)} not a delegate type.");

            return GenerateTrampoline(typeof(T).GetMethod("Invoke")).CreateDelegate(typeof(T)) as T;
        }

        // Used in RuntimeDetour legacy shim.
        internal T _GenerateTrampoline<T>() {
            if (IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");
            if (!typeof(Delegate).IsAssignableFrom(typeof(T)))
                throw new InvalidOperationException($"Type {typeof(T)} not a delegate type.");

            return (T) (object) GenerateTrampoline(typeof(T).GetMethod("Invoke")).CreateDelegate(typeof(T));
        }

        private DynamicMethod _GenerateNativeProxy(MethodBase signature) {
            // Generate a method to call the native function.
            // Effectively a "proxy" into the native space.
            // This is invoked by the trampoline.

            Type returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);

            ParameterInfo[] args = signature.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            DynamicMethod dm = new DynamicMethod(
                $"native_{((long) Data.Method).ToString("X16")}_{GetHashCode()}",
                returnType, argTypes,
                true
            );
            ILGenerator il = dm.GetILGenerator();

            for (int i = 128; i > -1; --i)
                il.Emit(OpCodes.Nop);
            if (returnType != typeof(void)) {
                il.Emit(OpCodes.Ldnull);
                if (returnType.IsValueType)
                    il.Emit(OpCodes.Box, returnType);
            }
            il.Emit(OpCodes.Ret);

            // Detour it.
            NativeDetourData detour = DetourManager.Native.Create(dm.GetJITStart(), Data.Method);
            DetourManager.Native.Apply(detour);
            DetourManager.Native.Free(detour);

            return dm;
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
