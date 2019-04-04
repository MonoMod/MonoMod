using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using MonoMod.Utils;
using Mono.Cecil.Cil;

namespace MonoMod.RuntimeDetour {
    public class Hook : IDetour {

        public static Func<Hook, MethodBase, MethodBase, object, bool> OnDetour;
        public static Func<Hook, bool> OnUndo;
        public static Func<Hook, MethodBase, MethodBase> OnGenerateTrampoline;

        public bool IsValid => _Detour.IsValid;

        public readonly MethodBase Method;
        public readonly MethodBase Target;

        private MethodInfo _Hook;
        private Detour _Detour;

        private readonly Type _OrigDelegateType;
        private readonly MethodInfo _OrigDelegateInvoke;

        private int? _RefTarget;
        private int? _RefTrampoline;
        private int? _RefTrampolineTmp;

        public Hook(MethodBase from, MethodInfo to, object target) {
            Method = from;
            _Hook = to;

            if (!(OnDetour?.InvokeWhileTrue(this, from, to, target) ?? true))
                return;

            // Check if hook ret -> method ret is valid. Don't check for method ret -> hook ret, as that's too strict.
            Type returnType = (from as MethodInfo)?.ReturnType ?? typeof(void);
            if (_Hook.ReturnType != returnType && !_Hook.ReturnType.IsCompatible(returnType))
                throw new InvalidOperationException($"Return type of hook for {from} doesn't match, must be {((from as MethodInfo)?.ReturnType ?? typeof(void)).FullName}");

            if (target == null && !to.IsStatic) {
                throw new InvalidOperationException($"Hook for method {from} must be static, or you must pass a target instance.");
            }

            ParameterInfo[] hookArgs = _Hook.GetParameters();

            // Check if the parameters match.
            // If the delegate has got an extra first parameter that itself is a delegate, it's the orig trampoline passthrough.
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

            Type origType = null;
            if (hookArgs.Length == argTypes.Length + 1 && typeof(Delegate).IsAssignableFrom(hookArgs[0].ParameterType))
                _OrigDelegateType = origType = hookArgs[0].ParameterType;
            else if (hookArgs.Length != argTypes.Length)
                throw new InvalidOperationException($"Parameter count of hook for {from} doesn't match, must be {argTypes.Length}");

            for (int i = 0; i < argTypes.Length; i++) {
                Type argMethod = argTypes[i];
                Type argHook = hookArgs[i + (origType == null ? 0 : 1)].ParameterType;
                if (!argMethod.IsCompatible(argHook))
                    throw new InvalidOperationException($"Parameter #{i} of hook for {from} doesn't match, must be {argMethod.FullName} or related");
            }

            MethodInfo origInvoke = _OrigDelegateInvoke = origType?.GetMethod("Invoke");
            // TODO: Check origType Invoke arguments.

            DynamicMethodDefinition dmd;
            ILProcessor il;

            using (dmd = new DynamicMethodDefinition(
                $"Hook<{Method.GetFindableID(simple: true)}>?{GetHashCode()}",
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes
            )) {
                il = dmd.GetILProcessor();

                if (target != null) {
                    _RefTarget = il.EmitReference(target);
                }

                if (origType != null) {
                    _RefTrampoline = il.EmitReference<Delegate>(null);
                }

                // TODO: Use specialized Ldarg.* if possible; What about ref types?
                for (int i = 0; i < argTypes.Length; i++)
                    il.Emit(OpCodes.Ldarg, i);

                il.Emit(OpCodes.Call, _Hook);

                il.Emit(OpCodes.Ret);

                Target = dmd.Generate().Pin();
            }

            // Temporarily provide a trampoline that waits for the proper trampoline.
            if (origType != null) {
                ParameterInfo[] origArgs = origInvoke.GetParameters();
                Type[] origArgTypes = new Type[origArgs.Length];
                for (int i = 0; i < origArgs.Length; i++)
                    origArgTypes[i] = origArgs[i].ParameterType;

                using (dmd = new DynamicMethodDefinition(
                    $"Chain:TMP<{Method.GetFindableID(simple: true)}>?{GetHashCode()}",
                    (origInvoke as MethodInfo)?.ReturnType ?? typeof(void), origArgTypes
                )) {
                    il = dmd.GetILProcessor();

                    // while (ref == null) { }
                    _RefTrampolineTmp = il.EmitReference<Delegate>(null);
                    il.Emit(OpCodes.Brfalse, il.Body.Instructions[0]);

                    // Invoke the generated delegate.
                    il.EmitGetReference<Delegate>(_RefTrampolineTmp.Value);

                    // TODO: Use specialized Ldarg.* if possible; What about ref types?
                    for (int i = 0; i < argTypes.Length; i++)
                        il.Emit(OpCodes.Ldarg, i);

                    il.Emit(OpCodes.Callvirt, origInvoke);

                    il.Emit(OpCodes.Ret);

                    DynamicMethodHelper.SetReference(_RefTrampoline.Value, dmd.Generate().CreateDelegate(origType));
                }
            }

            _Detour = new Detour(Method, Target);

            UpdateOrig(null);
        }
        public Hook(MethodBase from, MethodInfo to)
            : this(from, to, null) {
        }

        public Hook(MethodBase method, IntPtr to)
            : this(method, DetourHelper.GenerateNativeProxy(to, method), null) {
        }
        public Hook(MethodBase method, Delegate to)
            : this(method, to.GetMethodInfo(), to.Target) {
        }

        public Hook(Delegate from, IntPtr to)
            : this(from.GetMethodInfo(), to) {
        }
        public Hook(Delegate from, Delegate to)
            : this(from.GetMethodInfo(), to) {
        }

        public Hook(Expression from, IntPtr to)
            : this(((MethodCallExpression) from).Method, to) {
        }
        public Hook(Expression from, Delegate to)
            : this(((MethodCallExpression) from).Method, to) {
        }

        public Hook(Expression<Action> from, IntPtr to)
            : this(from.Body, to) {
        }
        public Hook(Expression<Action> from, Delegate to)
            : this(from.Body, to) {
        }

        public void Apply() {
            _Detour.Apply();
        }

        public void Undo() {
            if (!(OnUndo?.InvokeWhileTrue(this) ?? true))
                return;

            _Detour.Undo();
            if (!IsValid)
                _Free();
        }

        public void Free() {
            if (!IsValid)
                return;

            _Detour.Free();
            _Free();
        }

        public void Dispose() {
            Undo();
            Free();
        }

        private void _Free() {
            if (_RefTarget != null)
                DynamicMethodHelper.FreeReference(_RefTarget.Value);
            if (_RefTrampoline != null)
                DynamicMethodHelper.FreeReference(_RefTrampoline.Value);
            if (_RefTrampolineTmp != null)
                DynamicMethodHelper.FreeReference(_RefTrampolineTmp.Value);
        }

        public MethodBase GenerateTrampoline(MethodBase signature = null) {
            MethodBase remoteTrampoline = OnGenerateTrampoline?.InvokeWhileNull<MethodBase>(this, signature);
            if (remoteTrampoline != null)
                return remoteTrampoline;

            return _Detour.GenerateTrampoline(signature);
        }

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
        public T GenerateTrampoline<T>() where T : Delegate {
            if (!typeof(Delegate).IsAssignableFrom(typeof(T)))
                throw new InvalidOperationException($"Type {typeof(T)} not a delegate type.");

            return GenerateTrampoline(typeof(T).GetMethod("Invoke")).CreateDelegate(typeof(T)) as T;
        }

        // Used by HookEndpoint for the low level IL manip.
        internal void UpdateOrig(MethodBase invoke) {
            if (_OrigDelegateType == null)
                return;
            Delegate orig = (invoke ?? GenerateTrampoline(_OrigDelegateInvoke)).CreateDelegate(_OrigDelegateType);
            DynamicMethodHelper.SetReference(_RefTrampoline.Value, orig);
            DynamicMethodHelper.SetReference(_RefTrampolineTmp.Value, orig);
        }
    }

    public class Hook<T> : Hook {
        public Hook(Expression<Action> from, T to)
            : base(from.Body, to as Delegate) {
        }

        public Hook(Expression<Func<T>> from, IntPtr to)
            : base(from.Body, to) {
        }
        public Hook(Expression<Func<T>> from, Delegate to)
            : base(from.Body, to) {
        }

        public Hook(T from, IntPtr to)
            : base(from as Delegate, to) {
        }
        public Hook(T from, T to)
            : base(from as Delegate, to as Delegate) {
        }
    }

    public class Hook<TFrom, TTo> : Hook {
        public Hook(Expression<Func<TFrom>> from, TTo to)
            : base(from.Body, to as Delegate) {
        }

        public Hook(TFrom from, TTo to)
            : base(from as Delegate, to as Delegate) {
        }
    }
}
