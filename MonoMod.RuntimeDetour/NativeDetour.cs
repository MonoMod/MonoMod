using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour {
    /// <summary>
    /// The data forming a "raw" native detour, created and consumed by DetourManager.Native.
    /// </summary>
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
        /// The type of the detour. Determined when the structure is created by the IDetourNativePlatform.
        /// </summary>
        public byte Type;

        /// <summary>
        /// The size of the detour. Calculated when the structure is created by the IDetourNativePlatform.
        /// </summary>
        public uint Size;

        /// <summary>
        /// DetourManager.Native-specific data.
        /// </summary>
        public IntPtr Extra;
    }

    /// <summary>
    /// A "raw" native detour, acting as a wrapper around NativeDetourData with a few helpers.
    /// Only one NativeDetour for a method to detour from can exist at any given time. NativeDetours cannot be layered.
    /// If you don't need the trampoline generator or any of the management helpers, use DetourManager.Native directly.
    /// Unless you're writing your own detour manager or need to detour native functions, it's better to create instances of Detour instead.
    /// </summary>
    public class NativeDetour : IDetour {

        public static Func<NativeDetour, MethodBase, IntPtr, IntPtr, bool> OnDetour;
        public static Func<NativeDetour, bool> OnUndo;
        public static Func<NativeDetour, MethodBase, MethodBase> OnGenerateTrampoline;

        public bool IsValid => !_IsFree;

        public readonly NativeDetourData Data;
        public readonly MethodBase Method;

        private readonly MethodInfo _BackupMethod;
        private readonly IntPtr _BackupNative;

        private bool _IsFree;

        public NativeDetour(MethodBase method, IntPtr from, IntPtr to) {
            Method = method;

            if (!(OnDetour?.InvokeWhileTrue(this, method, from, to) ?? true))
                return;

            Data = DetourHelper.Native.Create(from, to, null);

            // Backing up the original function only needs to happen once.
            method?.TryCreateILCopy(out _BackupMethod);

            // BackupNative is required even if BackupMethod is present to undo the detour.
            _BackupNative = DetourHelper.Native.MemAlloc(Data.Size);
            DetourHelper.Native.Copy(Data.Method, _BackupNative, Data.Type);

            Apply();
        }

        public NativeDetour(IntPtr from, IntPtr to)
            : this(null, from, to) {
        }
        public NativeDetour(MethodBase from, IntPtr to)
            : this(from, from.GetNativeStart(), to) {
        }

        public NativeDetour(IntPtr from, MethodBase to)
            : this(from, to.GetNativeStart()) {
        }
        public NativeDetour(MethodBase from, MethodBase to)
            : this(from, DetourHelper.Runtime.GetDetourTarget(from, to).GetNativeStart()) {
        }

        public NativeDetour(Delegate from, IntPtr to)
            : this(from.GetMethodInfo(), to) {
        }
        public NativeDetour(IntPtr from, Delegate to)
            : this(from, to.GetMethodInfo()) {
        }
        public NativeDetour(Delegate from, Delegate to)
            : this(from.GetMethodInfo(), to.GetMethodInfo()) {
        }

        /// <summary>
        /// Apply the native detour. This automatically happens when creating an instance.
        /// </summary>
        public void Apply() {
            if (_IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            DetourHelper.Native.MakeWritable(Data);
            DetourHelper.Native.Apply(Data);
            DetourHelper.Native.MakeExecutable(Data);
        }

        /// <summary>
        /// Undo the native detour. Doesn't free the detour native data, allowing you to reapply it later.
        /// </summary>
        public void Undo() {
            if (!(OnUndo?.InvokeWhileTrue(this) ?? true))
                return;

            if (_IsFree)
                return;

            DetourHelper.Native.Copy(_BackupNative, Data.Method, Data.Type);
        }

        /// <summary>
        /// Free the detour's data without undoing it. This makes any further operations on this detour invalid.
        /// </summary>
        public void Free() {
            if (_IsFree)
                return;
            _IsFree = true;

            DetourHelper.Native.MemFree(_BackupNative);
            DetourHelper.Native.Free(Data);
        }

        /// <summary>
        /// Undo and free this temporary detour.
        /// </summary>
        public void Dispose() {
            Undo();
            Free();
        }

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// If the NativeDetour holds a reference to a managed method, a copy of the original method is returned.
        /// If the NativeDetour holds a reference to a native function, an "undo-call-redo" trampoline with a matching signature is returned.
        /// </summary>
        public MethodBase GenerateTrampoline(MethodBase signature = null) {
            MethodBase remoteTrampoline = OnGenerateTrampoline?.InvokeWhileNull<MethodBase>(this, signature);
            if (remoteTrampoline != null)
                return remoteTrampoline;

            if (_IsFree)
                throw new InvalidOperationException("Free() has been called on this detour.");

            if (_BackupMethod != null) {
                // If we're detouring an IL method and have an IL copy, invoke the IL copy.
                // Note that this ignores the passed signature.
                return _BackupMethod;
            }

            /*
            if (signature == null)
                signature = _BackupMethod;
            */
            if (signature == null)
                throw new ArgumentNullException("A signature must be given if the NativeDetour doesn't hold a reference to a managed method.");

            // Otherwise, undo the detour, call the method and reapply the detour.

            MethodBase methodCallable = Method;
            if (methodCallable == null) {
                methodCallable = DetourHelper.GenerateNativeProxy(Data.Method, signature);
            }

            Type returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);

            ParameterInfo[] args = signature.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"Trampoline:Native<{Method?.GetFindableID(simple: true) ?? ((long) Data.Method).ToString("X16")}>?{GetHashCode()}",
                returnType, argTypes
            )) {
                ILProcessor il = dmd.GetILProcessor();

                ExceptionHandler eh = new ExceptionHandler(ExceptionHandlerType.Finally);
                il.Body.ExceptionHandlers.Add(eh);

                il.EmitDetourCopy(_BackupNative, Data.Method, Data.Type);

                // Store the return value in a local as we can't preserve the stack across exception block boundaries.
                VariableDefinition localResult = null;
                if (returnType != typeof(void))
                    il.Body.Variables.Add(localResult = new VariableDefinition(il.Import(returnType)));

                // Label blockTry = il.BeginExceptionBlock();
                int instriTryStart = il.Body.Instructions.Count;

                for (int i = 0; i < argTypes.Length; i++)
                    il.Emit(OpCodes.Ldarg, i);

                if (methodCallable is MethodInfo)
                    il.Emit(OpCodes.Call, (MethodInfo) methodCallable);
                else if (methodCallable is ConstructorInfo)
                    il.Emit(OpCodes.Call, (ConstructorInfo) methodCallable);
                else
                    throw new NotSupportedException($"Method type {methodCallable.GetType().FullName} not supported.");

                if (localResult != null)
                    il.Emit(OpCodes.Stloc, localResult);

                il.Emit(OpCodes.Leave, (object) null);
                Instruction instrLeave = il.Body.Instructions[il.Body.Instructions.Count - 1];

                // il.BeginFinallyBlock();
                int instriTryEnd = il.Body.Instructions.Count;
                int instriFinallyStart = il.Body.Instructions.Count;

                // Reapply the detour even if the method threw an exception.
                il.EmitDetourApply(Data);

                // il.EndExceptionBlock();
                int instriFinallyEnd = il.Body.Instructions.Count;

                Instruction instrLeaveTarget = null;

                if (localResult != null) {
                    il.Emit(OpCodes.Ldloc, localResult);
                    instrLeaveTarget = il.Body.Instructions[il.Body.Instructions.Count - 1];
                }

                il.Emit(OpCodes.Ret);
                instrLeaveTarget = instrLeaveTarget ?? il.Body.Instructions[il.Body.Instructions.Count - 1];
                instrLeave.Operand = instrLeaveTarget;

                // TODO: Are the exception handler indices correct?
                eh.TryStart = il.Body.Instructions[instriTryStart];
                eh.TryEnd = il.Body.Instructions[instriTryEnd];
                eh.HandlerStart = il.Body.Instructions[instriTryEnd];
                eh.HandlerEnd = il.Body.Instructions[instriFinallyEnd];

                return dmd.Generate().Pin();
            }
        }

        /// <summary>
        /// Generate a new delegate with which you can invoke the previous state.
        /// If the NativeDetour holds a reference to a managed method, a copy of the original method is returned.
        /// If the NativeDetour holds a reference to a native function, an "undo-call-redo" trampoline with a matching signature is returned.
        /// </summary>
        public T GenerateTrampoline<T>() where T : Delegate {
            if (!typeof(Delegate).IsAssignableFrom(typeof(T)))
                throw new InvalidOperationException($"Type {typeof(T)} not a delegate type.");

            return GenerateTrampoline(typeof(T).GetMethod("Invoke")).CreateDelegate(typeof(T)) as T;
        }
    }
}
