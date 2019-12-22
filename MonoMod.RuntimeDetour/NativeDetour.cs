using System;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;

namespace MonoMod.RuntimeDetour {
    public struct NativeDetourConfig {
        public bool ManualApply;
        public bool SkipILCopy;
    }

    /// <summary>
    /// A "raw" native detour, acting as a wrapper around NativeDetourData with a few helpers.
    /// Only one NativeDetour for a method to detour from can exist at any given time. NativeDetours cannot be layered.
    /// If you don't need the trampoline generator or any of the management helpers, use DetourManager.Native directly.
    /// Unless you're writing your own detour manager or need to detour native functions, it's better to create instances of Detour instead.
    /// </summary>
    public unsafe class NativeDetour : IDetour {

        public static Func<NativeDetour, MethodBase, IntPtr, IntPtr, bool> OnDetour;
        public static Func<NativeDetour, bool> OnUndo;
        public static Func<NativeDetour, MethodBase, MethodBase> OnGenerateTrampoline;

        public bool IsValid { get; private set; }
        public bool IsApplied { get; private set; }

        public readonly NativeDetourData Data;
        public readonly MethodBase Method;

        private readonly MethodInfo _BackupMethod;
        private readonly IntPtr _BackupNative;

        private HashSet<MethodBase> _Pinned = new HashSet<MethodBase>();

        public NativeDetour(MethodBase method, IntPtr from, IntPtr to, ref NativeDetourConfig config) {
            Method = method;

            if (!(OnDetour?.InvokeWhileTrue(this, method, from, to) ?? true))
                return;
            IsValid = true;

            Data = DetourHelper.Native.Create(from, to, null);

            // Backing up the original function only needs to happen once.
            if (!config.SkipILCopy)
                method?.TryCreateILCopy(out _BackupMethod);

            // BackupNative is required even if BackupMethod is present to undo the detour.
            _BackupNative = DetourHelper.Native.MemAlloc(Data.Size);

            if (!config.ManualApply)
                Apply();
        }
        public NativeDetour(MethodBase method, IntPtr from, IntPtr to, NativeDetourConfig config)
            : this(method, from, to, ref config) {
        }
        public NativeDetour(MethodBase method, IntPtr from, IntPtr to)
            : this(method, from, to, default) {
        }

        public NativeDetour(IntPtr from, IntPtr to, ref NativeDetourConfig config)
            : this(null, from, to, ref config) {
        }
        public NativeDetour(IntPtr from, IntPtr to, NativeDetourConfig config)
            : this(null, from, to, ref config) {
        }
        public NativeDetour(IntPtr from, IntPtr to)
            : this(null, from, to) {
        }
        public NativeDetour(MethodBase from, IntPtr to, ref NativeDetourConfig config)
            : this(from, from.Pin().GetNativeStart(), to, ref config) {
            _Pinned.Add(from);
        }
        public NativeDetour(MethodBase from, IntPtr to, NativeDetourConfig config)
            : this(from, from.Pin().GetNativeStart(), to, ref config) {
            _Pinned.Add(from);
        }
        public NativeDetour(MethodBase from, IntPtr to)
            : this(from, from.Pin().GetNativeStart(), to) {
            _Pinned.Add(from);
        }

        public NativeDetour(IntPtr from, MethodBase to, ref NativeDetourConfig config)
            : this(from, to.Pin().GetNativeStart(), ref config) {
            _Pinned.Add(to);
        }
        public NativeDetour(IntPtr from, MethodBase to, NativeDetourConfig config)
            : this(from, to.Pin().GetNativeStart(), ref config) {
            _Pinned.Add(to);
        }
        public NativeDetour(IntPtr from, MethodBase to)
            : this(from, to.Pin().GetNativeStart()) {
            _Pinned.Add(to);
        }
        public NativeDetour(MethodBase from, MethodBase to, ref NativeDetourConfig config)
            : this(from, DetourHelper.Runtime.GetDetourTarget(from, to).Pin().GetNativeStart(), ref config) {
            _Pinned.Add(to);
        }
        public NativeDetour(MethodBase from, MethodBase to, NativeDetourConfig config)
            : this(from, DetourHelper.Runtime.GetDetourTarget(from, to).Pin().GetNativeStart(), ref config) {
            _Pinned.Add(to);
        }
        public NativeDetour(MethodBase from, MethodBase to)
            : this(from, DetourHelper.Runtime.GetDetourTarget(from, to).Pin().GetNativeStart()) {
            _Pinned.Add(to);
        }

        public NativeDetour(Delegate from, IntPtr to, ref NativeDetourConfig config)
            : this(from.Method, to, ref config) {
        }
        public NativeDetour(Delegate from, IntPtr to, NativeDetourConfig config)
            : this(from.Method, to, ref config) {
        }
        public NativeDetour(Delegate from, IntPtr to)
            : this(from.Method, to) {
        }
        public NativeDetour(IntPtr from, Delegate to, ref NativeDetourConfig config)
            : this(from, to.Method, ref config) {
        }
        public NativeDetour(IntPtr from, Delegate to, NativeDetourConfig config)
            : this(from, to.Method, ref config) {
        }
        public NativeDetour(IntPtr from, Delegate to)
            : this(from, to.Method) {
        }
        public NativeDetour(Delegate from, Delegate to, ref NativeDetourConfig config)
            : this(from.Method, to.Method, ref config) {
        }
        public NativeDetour(Delegate from, Delegate to, NativeDetourConfig config)
            : this(from.Method, to.Method, ref config) {
        }
        public NativeDetour(Delegate from, Delegate to)
            : this(from.Method, to.Method) {
        }

        /// <summary>
        /// Apply the native detour. This can be done automatically when creating an instance.
        /// </summary>
        public void Apply() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(NativeDetour));

            if (IsApplied)
                return;
            IsApplied = true;

            DetourHelper.Native.Copy(Data.Method, _BackupNative, Data.Type);

            DetourHelper.Native.MakeWritable(Data);
            DetourHelper.Native.Apply(Data);
            DetourHelper.Native.MakeExecutable(Data);
            DetourHelper.Native.FlushICache(Data);
        }

        /// <summary>
        /// Undo the native detour without freeing the detour native data, allowing you to reapply it later.
        /// </summary>
        public void Undo() {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(NativeDetour));

            if (!(OnUndo?.InvokeWhileTrue(this) ?? true))
                return;

            if (!IsApplied)
                return;
            IsApplied = false;

            DetourHelper.Native.MakeWritable(Data);
            DetourHelper.Native.Copy(_BackupNative, Data.Method, Data.Type);
            DetourHelper.Native.MakeExecutable(Data);
            DetourHelper.Native.FlushICache(Data);
        }

        /// <summary>
        /// Free the detour's data without undoing it. This makes any further operations on this detour invalid.
        /// </summary>
        public void Free() {
            if (!IsValid)
                return;
            IsValid = false;

            DetourHelper.Native.MemFree(_BackupNative);
            DetourHelper.Native.Free(Data);

            if (!IsApplied) {
                foreach (MethodBase method in _Pinned)
                    method.Unpin();
                _Pinned.Clear();
            }
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
        /// If the NativeDetour holds a reference to a managed method, a copy of the original method is returned.
        /// If the NativeDetour holds a reference to a native function, an "undo-call-redo" trampoline with a matching signature is returned.
        /// </summary>
        public MethodBase GenerateTrampoline(MethodBase signature = null) {
            MethodBase remoteTrampoline = OnGenerateTrampoline?.InvokeWhileNull<MethodBase>(this, signature);
            if (remoteTrampoline != null)
                return remoteTrampoline;

            if (!IsValid)
                throw new ObjectDisposedException(nameof(NativeDetour));

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
                $"Trampoline:Native<{Method?.GetID(simple: true) ?? ((long) Data.Method).ToString("X16")}>?{GetHashCode()}",
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

                return dmd.Generate();
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
