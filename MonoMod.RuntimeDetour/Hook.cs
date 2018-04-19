using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;

namespace MonoMod.RuntimeDetour {
    public class Hook : IDetour {

        public bool IsValid => _Detour.IsValid;

        public readonly MethodBase Method;
        public readonly MethodBase Target;

        private MethodInfo _Hook;
        private Detour _Detour;

        private int? _RefTarget;
        private int? _RefTrampoline;

        public Hook(MethodBase from, MethodInfo to, object target) {
            Method = from;
            _Hook = to;

            if (_Hook.ReturnType != ((from as MethodInfo)?.ReturnType ?? typeof(void)))
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
                argTypes[0] = Method.DeclaringType;
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            Type origType = null;
            if (hookArgs.Length == argTypes.Length + 1 && typeof(Delegate).IsAssignableFrom(hookArgs[0].ParameterType))
                origType = hookArgs[0].ParameterType;
            else if (hookArgs.Length != argTypes.Length)
                throw new InvalidOperationException($"Parameter count of hook for {from} doesn't match, must be {argTypes.Length}");

            for (int i = 0; i < argTypes.Length; i++) {
                Type argMethod = argTypes[i];
                Type argHook = hookArgs[i + (origType == null ? 0 : 1)].ParameterType;
                if (!argMethod.IsAssignableFrom(argHook) &&
                    !argHook.IsAssignableFrom(argMethod))
                    throw new InvalidOperationException($"Parameter #{i} of hook for {from} doesn't match, must be {argMethod.FullName} or related");
            }

            MethodInfo origInvoke = origType?.GetMethod("Invoke");
            // TODO: Check origType Invoke arguments.

            DynamicMethod dm;
            ILGenerator il;

            DynamicMethod trampoline = null;
            if (origType != null) {
                trampoline = new DynamicMethod(
                    $"trampoline_{Method.Name}_{GetHashCode()}",
                    _Hook.ReturnType, argTypes,
                    Method.DeclaringType,
                    false // Otherwise just ret is invalid for whatever reason.
                ).StubCriticalDetour().Pin();
            }

            dm = new DynamicMethod(
                $"hook_{Method.Name}_{GetHashCode()}",
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                Method.DeclaringType,
                true
            );
            il = dm.GetILGenerator();

            if (target != null) {
                _RefTarget = il.EmitReference(target);
            }

            if (trampoline != null) {
                _RefTrampoline = il.EmitReference(trampoline.CreateDelegate(origType));
            }

            // TODO: Use specialized Ldarg.* if possible; What about ref types?
            for (int i = 0; i < argTypes.Length; i++)
                il.Emit(OpCodes.Ldarg, i);

            il.Emit(OpCodes.Call, _Hook);

            il.Emit(OpCodes.Ret);

            Target = dm.Pin();

            _Detour = new Detour(Method, Target);
            
            if (trampoline != null) {
                NativeDetourData link = DetourManager.Native.Create(
                    trampoline.GetNativeStart(),
                    GenerateTrampoline(origInvoke).GetNativeStart()
                );
                DetourManager.Native.MakeWritable(link);
                DetourManager.Native.Apply(link);
                DetourManager.Native.MakeExecutable(link);
                DetourManager.Native.Free(link);
            }
        }
        public Hook(MethodBase from, MethodInfo to)
            : this(from, to, null) {
        }

        public Hook(MethodBase method, IntPtr to)
            : this(method, DetourManager.GenerateNativeProxy(to, method), null) {
        }
        public Hook(MethodBase method, Delegate to)
            : this(method, to.Method, to.Target) {
        }

        public Hook(Delegate from, IntPtr to)
            : this(from.Method, to) {
        }
        public Hook(Delegate from, Delegate to)
            : this(from.Method, to) {
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
            if (!IsValid)
                throw new InvalidOperationException("This hook has been undone.");

            _Detour.Apply();
        }

        public void Undo() {
            if (!IsValid)
                throw new InvalidOperationException("This hook has been undone.");

            _Detour.Undo();
            if (!IsValid)
                _Free();
        }

        public void Free() {
            if (!IsValid)
                throw new InvalidOperationException("This hook has been undone.");

            _Detour.Free();
            _Free();
        }

        private void _Free() {
            if (_RefTarget != null)
                DynamicMethodHelper.FreeReference(_RefTarget.Value);
            if (_RefTrampoline != null)
                DynamicMethodHelper.FreeReference(_RefTrampoline.Value);
        }

        public MethodBase GenerateTrampoline(MethodBase signature = null) {
            if (!IsValid)
                throw new InvalidOperationException("This hook has been undone.");

            return _Detour.GenerateTrampoline(signature);
        }

        /// <summary>
        /// Generate a new DynamicMethod with which you can invoke the previous state.
        /// </summary>
        public T GenerateTrampoline<T>() where T : class {
            if (!IsValid)
                throw new InvalidOperationException("This hook has been undone.");

            return _Detour.GenerateTrampoline<T>();
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
