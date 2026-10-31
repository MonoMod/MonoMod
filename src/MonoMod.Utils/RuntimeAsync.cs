using Mono.Cecil.Cil;
using Mono.Cecil;
using MonoMod.Backports;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using ReflOpCodes = System.Reflection.Emit.OpCodes;
using CecilOpCodes = Mono.Cecil.Cil.OpCodes;

namespace MonoMod.Utils
{
    /// <summary>
    /// A collection of helpers for processing and emitting IL for the runtime's async machinery.
    /// </summary>
    public static class RuntimeAsync
    {
        private sealed class ReflHolder
        {
            public readonly Type T_AsyncHelpers;

            public readonly Type T_Task;
            public readonly Type T_TaskT;
            public readonly Type T_ConfiguredTaskAwaitable;
            public readonly Type T_ConfiguredTaskAwaitableT;

            public readonly Type T_ValueTask;
            public readonly Type T_ValueTaskT;
            public readonly Type T_ConfiguredValueTaskAwaitable;
            public readonly Type T_ConfiguredValueTaskAwaitableT;

            public readonly Type T_INotifyCompletion;
            public readonly Type T_ICriticalNotifyCompletion;

            // void Await(Task)
            public readonly MethodInfo Await_Task;
            // T Await<T>(Task<T>)
            public readonly MethodInfo Await_TaskT;
            // void Await(ValueTask)
            public readonly MethodInfo Await_ValueTask;
            // T Await<T>(ValueTask<T>)
            public readonly MethodInfo Await_ValueTaskT;
            // void Await(ConfiguredTaskAwaitable)
            public readonly MethodInfo Await_ConfiguredTaskAwaitable;
            // void Await(ConfiguredValueTaskAwaitable)
            public readonly MethodInfo Await_ConfiguredValueTaskAwaitable;
            // T Await<T>(ConfiguredTaskAwaitable<T>)
            public readonly MethodInfo Await_ConfiguredTaskAwaitableT;
            // T Await<T>(ConfiguredValueTaskAwaitable<T>)
            public readonly MethodInfo Await_ConfiguredValueTaskAwaitableT;
            // void AwaitAwaiter<TAwaiter>(TAwaiter) where TAwaiter : INotifyCompletion
            public readonly MethodInfo AwaitAwaiter;
            // void UnsafeAwaitAwaiter<TAwaiter>(TAwaiter) where TAwaiter : ICriticalNotifyCompletion
            public readonly MethodInfo UnsafeAwaitAwaiter;


            public ReflHolder(Type t_AsyncHelpers)
            {
                T_AsyncHelpers = t_AsyncHelpers;

                foreach (var method in T_AsyncHelpers.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name is "Await")
                    {
                        if (method.ContainsGenericParameters)
                        {
                            // one of the typed methods
                            var paramType = method.GetParameters()[0].ParameterType;
                            switch (paramType.Name)
                            {
                                case "Task`1":
                                    T_TaskT = paramType.GetGenericTypeDefinition();
                                    Await_TaskT = method;
                                    break;
                                case "ValueTask`1":
                                    T_ValueTaskT = paramType.GetGenericTypeDefinition();
                                    Await_ValueTaskT = method;
                                    break;
                                case "ConfiguredTaskAwaitable`1":
                                    T_ConfiguredTaskAwaitableT = paramType.GetGenericTypeDefinition();
                                    Await_ConfiguredTaskAwaitableT = method;
                                    break;
                                case "ConfiguredValueTaskAwaitable`1":
                                    T_ConfiguredValueTaskAwaitableT = paramType.GetGenericTypeDefinition();
                                    Await_ConfiguredValueTaskAwaitableT = method;
                                    break;

                                default:
                                    MMDbgLog.Warning($"Unrecognized AsyncHelpers.Await method: {method}");
                                    continue;
                            }
                        }
                        else
                        {
                            // one of the untyped methods
                            var paramType = method.GetParameters()[0].ParameterType;
                            switch (paramType.Name)
                            {
                                case "Task":
                                    T_Task = paramType;
                                    Await_Task = method;
                                    break;
                                case "ValueTask":
                                    T_ValueTask = paramType;
                                    Await_ValueTask = method;
                                    break;
                                case "ConfiguredTaskAwaitable":
                                    T_ConfiguredTaskAwaitable = paramType;
                                    Await_ConfiguredTaskAwaitable = method;
                                    break;
                                case "ConfiguredValueTaskAwaitable":
                                    T_ConfiguredValueTaskAwaitable = paramType;
                                    Await_ConfiguredValueTaskAwaitable = method;
                                    break;

                                default:
                                    MMDbgLog.Warning($"Unrecognized AsyncHelpers.Await method: {method}");
                                    continue;
                            }
                        }
                    }
                    else if (method.Name is "AwaitAwaiter" or "UnsafeAwaitAwaiter")
                    {
                        if (!method.IsGenericMethodDefinition)
                        {
                            MMDbgLog.Warning($"Unrecognized AsyncHelpers.AwaitAwaiter method: {method}");
                            continue;
                        }

                        var constraintType = method.GetGenericArguments()[0].GetGenericParameterConstraints()[0];
                        if (method.Name is "AwaitAwaiter")
                        {
                            T_INotifyCompletion = constraintType;
                            AwaitAwaiter = method;
                        }
                        else
                        {
                            T_ICriticalNotifyCompletion = constraintType;
                            UnsafeAwaitAwaiter = method;
                        }
                    }
                }

                // types
                Helpers.Assert(T_Task is not null);
                Helpers.Assert(T_TaskT is not null);
                Helpers.Assert(T_ValueTask is not null);
                Helpers.Assert(T_ValueTaskT is not null);
                Helpers.Assert(T_ConfiguredTaskAwaitable is not null);
                Helpers.Assert(T_ConfiguredTaskAwaitableT is not null);
                Helpers.Assert(T_ConfiguredValueTaskAwaitable is not null);
                Helpers.Assert(T_ConfiguredValueTaskAwaitableT is not null);
                Helpers.Assert(T_INotifyCompletion is not null);
                Helpers.Assert(T_ICriticalNotifyCompletion is not null);

                // methods
                Helpers.Assert(Await_Task is not null);
                Helpers.Assert(Await_TaskT is not null);
                Helpers.Assert(Await_ValueTask is not null);
                Helpers.Assert(Await_ValueTaskT is not null);
                Helpers.Assert(Await_ConfiguredTaskAwaitable is not null);
                Helpers.Assert(Await_ConfiguredTaskAwaitableT is not null);
                Helpers.Assert(Await_ConfiguredValueTaskAwaitable is not null);
                Helpers.Assert(Await_ConfiguredValueTaskAwaitableT is not null);
                Helpers.Assert(AwaitAwaiter is not null);
                Helpers.Assert(UnsafeAwaitAwaiter is not null);
            }
        }

        private static readonly ReflHolder? Holder = TryCreateHolder();

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "We want failures here to manifest as IsSupported=false")]
        private static ReflHolder? TryCreateHolder()
        {
            try
            {
#if NET11_0_OR_GREATER
                return new(typeof(System.Runtime.CompilerServices.AsyncHelpers));
#else
                // AsyncHelpers is always in System.Private.CoreLib, if it's present at all
                return typeof(object).Assembly.GetType("System.Runtime.CompilerServices.AsyncHelpers") is { } ty ? new(ty) : null;
#endif
            }
            catch (Exception e)
            {
                MMDbgLog.Warning($"Exception while creating RuntimeAsync.ReflHolder: {e}");
                return null;
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> if the current runtime configuration supports runtime async.
        /// If this returns <see langword="false"/>, all other members on <see cref="RuntimeAsync"/> fail,
        /// even if the types they would return exist in this runtime configuration.
        /// </summary>
        [MemberNotNullWhen(true, nameof(Task))]
        [MemberNotNullWhen(true, nameof(TaskT))]
        [MemberNotNullWhen(true, nameof(ValueTask))]
        [MemberNotNullWhen(true, nameof(ValueTaskT))]
        [MemberNotNullWhen(true, nameof(ConfiguredTaskAwaitable))]
        [MemberNotNullWhen(true, nameof(ConfiguredTaskAwaitableT))]
        [MemberNotNullWhen(true, nameof(ConfiguredValueTaskAwaitable))]
        [MemberNotNullWhen(true, nameof(ConfiguredValueTaskAwaitableT))]
        [MemberNotNullWhen(true, nameof(INotifyCompletion))]
        [MemberNotNullWhen(true, nameof(ICriticalNotifyCompletion))]
        [MemberNotNullWhen(true, nameof(AwaitTask))]
        [MemberNotNullWhen(true, nameof(AwaitTaskT))]
        [MemberNotNullWhen(true, nameof(AwaitValueTask))]
        [MemberNotNullWhen(true, nameof(AwaitValueTaskT))]
        [MemberNotNullWhen(true, nameof(AwaitConfiguredTaskAwaitable))]
        [MemberNotNullWhen(true, nameof(AwaitConfiguredTaskAwaitableT))]
        [MemberNotNullWhen(true, nameof(AwaitConfiguredValueTaskAwaitable))]
        [MemberNotNullWhen(true, nameof(AwaitConfiguredValueTaskAwaitableT))]
        [MemberNotNullWhen(true, nameof(AwaitAwaiter))]
        [MemberNotNullWhen(true, nameof(UnsafeAwaitAwaiter))]
        public static bool IsSupported => Holder is not null;

        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private static ReflHolder GetHolder()
        {
            if (Holder is null)
            {
                [DoesNotReturn]
                static void Throw()
                    => throw new NotSupportedException("This runtime configuration does not support runtime async.");
                Throw();
            }
            return Holder;
        }

        // The crefs reference types which don't exist in most build configurations.
#pragma warning disable CA1200 // Avoid using cref tags with a prefix
        /// <summary>
        /// Specifies that a method uses the runtime's async machinery. Equivalent to <see cref="F:System.Runtime.CompilerServices.MethodImplOptions.Async"/>.
        /// </summary>
        public const MethodImplOptions AsyncIl = (MethodImplOptions)8192;

        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Threading.Tasks.Task"/>)</c>.
        /// </summary>
        public static Type Task => GetHolder().T_Task;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Threading.Tasks.Task`1"/>)</c>.
        /// </summary>
        public static Type TaskT => GetHolder().T_TaskT;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Threading.Tasks.ValueTask"/>)</c>.
        /// </summary>
        public static Type ValueTask => GetHolder().T_ValueTask;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Threading.Tasks.ValueTask`1"/>)</c>.
        /// </summary>
        public static Type ValueTaskT => GetHolder().T_ValueTaskT;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Runtime.CompilerServices.ConfiguredTaskAwaitable"/>)</c>.
        /// </summary>
        public static Type ConfiguredTaskAwaitable => GetHolder().T_ConfiguredTaskAwaitable;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1"/>)</c>.
        /// </summary>
        public static Type ConfiguredTaskAwaitableT => GetHolder().T_ConfiguredTaskAwaitableT;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable"/>)</c>.
        /// </summary>
        public static Type ConfiguredValueTaskAwaitable => GetHolder().T_ConfiguredValueTaskAwaitable;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1"/>)</c>.
        /// </summary>
        public static Type ConfiguredValueTaskAwaitableT => GetHolder().T_ConfiguredValueTaskAwaitableT;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Runtime.CompilerServices.INotifyCompletion"/>)</c>.
        /// </summary>
        public static Type INotifyCompletion => GetHolder().T_INotifyCompletion;
        /// <summary>
        /// Returns <c><see langword="typeof"/>(<see cref="T:System.Runtime.CompilerServices.ICriticalNotifyCompletion"/>)</c>.
        /// </summary>
        public static Type ICriticalNotifyCompletion => GetHolder().T_ICriticalNotifyCompletion;

        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await(System.Threading.Tasks.Task)"/>.
        /// </summary>
        public static MethodInfo AwaitTask => GetHolder().Await_Task;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await``1(System.Threading.Tasks.Task{``0})"/>.
        /// </summary>
        public static MethodInfo AwaitTaskT => GetHolder().Await_TaskT;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await(System.Threading.Tasks.ValueTask)"/>.
        /// </summary>
        public static MethodInfo AwaitValueTask => GetHolder().Await_ValueTask;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await``1(System.Threading.Tasks.ValueTask{``0})"/>.
        /// </summary>
        public static MethodInfo AwaitValueTaskT => GetHolder().Await_ValueTaskT;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await(System.Runtime.CompilerServices.ConfiguredTaskAwaitable)"/>.
        /// </summary>
        public static MethodInfo AwaitConfiguredTaskAwaitable => GetHolder().Await_ConfiguredTaskAwaitable;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await``1(System.Runtime.CompilerServices.ConfiguredTaskAwaitable{``0})"/>.
        /// </summary>
        public static MethodInfo AwaitConfiguredTaskAwaitableT => GetHolder().Await_ConfiguredTaskAwaitableT;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await(System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable)"/>.
        /// </summary>
        public static MethodInfo AwaitConfiguredValueTaskAwaitable => GetHolder().Await_ConfiguredValueTaskAwaitable;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.Await``1(System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable{``0})"/>.
        /// </summary>
        public static MethodInfo AwaitConfiguredValueTaskAwaitableT => GetHolder().Await_ConfiguredValueTaskAwaitableT;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.AwaitAwaiter``1(``0)"/>.
        /// </summary>
        public static MethodInfo AwaitAwaiter => GetHolder().AwaitAwaiter;
        /// <summary>
        /// Returns <see cref="M:System.Runtime.CompilerServices.AsyncHelpers.UnsafeAwaitAwaiter``1(``0)"/>.
        /// </summary>
        public static MethodInfo UnsafeAwaitAwaiter => GetHolder().UnsafeAwaitAwaiter;
#pragma warning restore CA1200 // Avoid using cref tags with a prefix

        /// <summary>
        /// Tests if <paramref name="type"/> is a supported runtime-async return type.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns><see langword="true"/> if <paramref name="type"/> is a supported runtime-async 
        /// return type; <see langword="false"/> if it is not.</returns>
        public static bool IsSupportedReturnType(Type type)
        {
            Helpers.ThrowIfArgumentNull(type);
            var holder = GetHolder();
            if (type.IsGenericType)
            {
                var genericTypeDef = type.GetGenericTypeDefinition();
                if (genericTypeDef == holder.T_TaskT)
                {
                    return true;
                }
                else if (genericTypeDef == holder.T_ValueTaskT)
                {
                    return true;
                }
            }
            else
            {
                if (type == holder.T_Task)
                {
                    return true;
                }
                else if (type == holder.T_ValueTask)
                {
                    return true;
                }
            }

            return false;
        }

        public static void EmitAwait(ILGenerator il, Type awaitableType)
        {
            Helpers.ThrowIfArgumentNull(il);
            Helpers.ThrowIfArgumentNull(awaitableType);

            var holder = GetHolder();
            if (awaitableType.IsGenericType)
            {
                var genericTypeDef = awaitableType.GetGenericTypeDefinition();
                if (genericTypeDef == holder.T_TaskT)
                {
                    var targ = awaitableType.GetGenericArguments()[0];
                    il.EmitCall(ReflOpCodes.Call, holder.Await_TaskT.MakeGenericMethod(targ), null);
                    return;
                }
                else if (genericTypeDef == holder.T_ValueTaskT)
                {
                    var targ = awaitableType.GetGenericArguments()[0];
                    il.EmitCall(ReflOpCodes.Call, holder.Await_ValueTaskT.MakeGenericMethod(targ), null);
                    return;
                }
                else if (genericTypeDef == holder.T_ConfiguredTaskAwaitableT)
                {
                    var targ = awaitableType.GetGenericArguments()[0];
                    il.EmitCall(ReflOpCodes.Call, holder.Await_ConfiguredTaskAwaitableT.MakeGenericMethod(targ), null);
                    return;
                }
                else if (genericTypeDef == holder.T_ConfiguredValueTaskAwaitableT)
                {
                    var targ = awaitableType.GetGenericArguments()[0];
                    il.EmitCall(ReflOpCodes.Call, holder.Await_ConfiguredValueTaskAwaitableT.MakeGenericMethod(targ), null);
                    return;
                }
            }
            else
            {
                if (awaitableType == holder.T_Task)
                {
                    il.EmitCall(ReflOpCodes.Call, holder.Await_Task, null);
                    return;
                }
                else if (awaitableType == holder.T_ValueTask)
                {
                    il.EmitCall(ReflOpCodes.Call, holder.Await_ValueTask, null);
                    return;
                }
                else if (awaitableType == holder.T_ConfiguredTaskAwaitable)
                {
                    il.EmitCall(ReflOpCodes.Call, holder.Await_ConfiguredTaskAwaitable, null);
                    return;
                }
                else if (awaitableType == holder.T_ConfiguredValueTaskAwaitable)
                {
                    il.EmitCall(ReflOpCodes.Call, holder.Await_ConfiguredValueTaskAwaitable, null);
                    return;
                }
            }

            [DoesNotReturn]
            static void ThrowNotAwaitable()
                => throw new ArgumentException("Provided type is not awaitable", nameof(awaitableType));

            // if we get here, then we have some unknown type of awaitable
            // an awaitable needs a .GetAwaiter(), so find that
            var getAwaiter = awaitableType.GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance, null, [], null);
            if (getAwaiter is null)
            {
                ThrowNotAwaitable();
            }

            var awaiterType = getAwaiter.ReturnType;
            // an awaiter must implement IsCompleted {get;} and GetResult()
            var isCompletedProp = awaiterType.GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance);
            var isCompletedGetter = isCompletedProp?.GetGetMethod();
            if (isCompletedGetter is null || isCompletedGetter.ReturnType != typeof(bool))
            {
                ThrowNotAwaitable();
            }

            var getResult = awaiterType.GetMethod("GetResult", BindingFlags.Public | BindingFlags.Instance, null, [], null);
            if (getResult is null)
            {
                ThrowNotAwaitable();
            }

            // an awaiter must implement INotifyCompletion or ICriticalNotifyCompletion
            MethodInfo awaitMethod;
            if (awaiterType.GetInterface(holder.T_ICriticalNotifyCompletion.FullName!) == holder.T_ICriticalNotifyCompletion)
            {
                // type implements ICriticalNotifyCompletion
                awaitMethod = holder.UnsafeAwaitAwaiter.MakeGenericMethod(awaiterType);
            }
            else if (awaiterType.GetInterface(holder.T_INotifyCompletion.FullName!) == holder.T_INotifyCompletion)
            {
                // type implements INotifyCompletion
                awaitMethod = holder.AwaitAwaiter.MakeGenericMethod(awaiterType);
            }
            else
            {
                ThrowNotAwaitable();
            }

            // we now have everything we need to emit the await
            // the pattern is:
            // var awaiter = (top-of-stack).GetAwaiter();
            // if (!awaiter.IsCompleted)
            // {
            //     AsyncHelpers.AwaitAwaiter(awaiter);
            // }
            // (top-of-stack) = awaiter.GetResult();

            var local = il.DeclareLocal(awaiterType);
            il.EmitCall(ReflOpCodes.Call, getAwaiter, null);
            il.Emit(ReflOpCodes.Stloc, local);

            var label = il.DefineLabel();
            if (awaiterType.IsValueType)
            {
                il.Emit(ReflOpCodes.Ldloca, local);
            }
            else
            {
                il.Emit(ReflOpCodes.Ldloc, local);
            }
            il.EmitCall(ReflOpCodes.Call, isCompletedGetter, null);
            il.Emit(ReflOpCodes.Brtrue, label);

            il.Emit(ReflOpCodes.Ldloc, local);
            il.EmitCall(ReflOpCodes.Call, awaitMethod, null);

            il.MarkLabel(label);
            if (awaiterType.IsValueType)
            {
                il.Emit(ReflOpCodes.Ldloca, local);
            }
            else
            {
                il.Emit(ReflOpCodes.Ldloc, local);
            }
            il.EmitCall(ReflOpCodes.Call, getResult, null);
        }


        private static MethodReference? FindPublicInstanceMethod(ModuleDefinition module, TypeReference type, string name)
        {
            var definition = type.SafeResolve();
            var method = definition?.FindMethodDeep(name);
            if (method is not null && method.IsPublic && !method.IsStatic && method.Parameters.Count == 0)
                return module.ImportReference(method, type);

            var reflectedMethod = type.ResolveReflection().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return reflectedMethod is null ? null : module.ImportReference(reflectedMethod);
        }

        private static MethodReference? FindPublicInstancePropertyGetter(ModuleDefinition module, TypeReference type, string name)
        {
            var definition = type.SafeResolve();
            var property = definition?.FindPropertyDeep(name);
            var getter = property?.GetMethod;
            if (getter is not null && getter.IsPublic && !getter.IsStatic && getter.Parameters.Count == 0)
                return module.ImportReference(getter, type);

            var reflectedGetter = type.ResolveReflection().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
            return reflectedGetter is null ? null : module.ImportReference(reflectedGetter);
        }

        private static bool ImplementsInterface(TypeReference type, Type interfaceType)
            => ImplementsInterface(type, interfaceType, new HashSet<TypeDefinition>());

        private static bool ImplementsInterface(TypeReference type, Type interfaceType, HashSet<TypeDefinition> visited)
        {
            var definition = type.SafeResolve();
            if (definition is null)
            {
                var reflectedType = type.ResolveReflection();
                return reflectedType.GetInterface(interfaceType.FullName!) == interfaceType;
            }

            if (!visited.Add(definition))
                return false;

            foreach (var interfaceImplementation in definition.Interfaces)
            {
                if (interfaceImplementation.InterfaceType.Is(interfaceType)
                    || ImplementsInterface(interfaceImplementation.InterfaceType, interfaceType, visited))
                    return true;
            }

            return definition.BaseType is { } baseType && ImplementsInterface(baseType, interfaceType, visited);
        }


        /// <summary>
        /// Emits the runtime await operation for a Cecil awaitable type into a Cecil method body.
        /// </summary>
        /// <param name="il">The IL processor to emit into.</param>
        /// <param name="awaitableType">The Cecil awaitable type to emit for.</param>
        public static void EmitAwait(ILProcessor il, TypeReference awaitableType)
        {
            Helpers.ThrowIfArgumentNull(il);
            Helpers.ThrowIfArgumentNull(awaitableType);

            var holder = GetHolder();
            var genericTypeDef = awaitableType is GenericInstanceType genericInstance
                ? genericInstance.ElementType
                : awaitableType;
            if (awaitableType is GenericInstanceType || awaitableType.HasGenericParameters)
            {
                var genericArgument = awaitableType is GenericInstanceType genericAwaitable
                    ? genericAwaitable.GenericArguments[0]
                    : awaitableType.GenericParameters[0];
                if (genericTypeDef.Is(holder.T_TaskT))
                {
                    EmitGenericCall(il, holder.Await_TaskT, genericArgument);
                    return;
                }
                else if (genericTypeDef.Is(holder.T_ValueTaskT))
                {
                    EmitGenericCall(il, holder.Await_ValueTaskT, genericArgument);
                    return;
                }
                else if (genericTypeDef.Is(holder.T_ConfiguredTaskAwaitableT))
                {
                    EmitGenericCall(il, holder.Await_ConfiguredTaskAwaitableT, genericArgument);
                    return;
                }
                else if (genericTypeDef.Is(holder.T_ConfiguredValueTaskAwaitableT))
                {
                    EmitGenericCall(il, holder.Await_ConfiguredValueTaskAwaitableT, genericArgument);
                    return;
                }
            }
            else
            {
                if (awaitableType.Is(holder.T_Task))
                {
                    il.Emit(CecilOpCodes.Call, holder.Await_Task);
                    return;
                }
                else if (awaitableType.Is(holder.T_ValueTask))
                {
                    il.Emit(CecilOpCodes.Call, holder.Await_ValueTask);
                    return;
                }
                else if (awaitableType.Is(holder.T_ConfiguredTaskAwaitable))
                {
                    il.Emit(CecilOpCodes.Call, holder.Await_ConfiguredTaskAwaitable);
                    return;
                }
                else if (awaitableType.Is(holder.T_ConfiguredValueTaskAwaitable))
                {
                    il.Emit(CecilOpCodes.Call, holder.Await_ConfiguredValueTaskAwaitable);
                    return;
                }
            }

            [DoesNotReturn]
            static void ThrowNotAwaitable()
                => throw new ArgumentException("Provided type is not awaitable", nameof(awaitableType));

            static void EmitGenericCall(ILProcessor il, MethodInfo method, TypeReference genericArgument)
            {
                var module = il.Body.Method.Module;
                var genericMethod = new GenericInstanceMethod(module.ImportReference(method));
                genericMethod.GenericArguments.Add(module.ImportReference(genericArgument));
                il.Emit(CecilOpCodes.Call, genericMethod);
            }

            var module = il.Body.Method.Module;
            var getAwaiter = FindPublicInstanceMethod(module, awaitableType, "GetAwaiter");
            if (getAwaiter is null)
                ThrowNotAwaitable();

            var awaiterType = getAwaiter.ReturnType;
            var isCompletedGetter = FindPublicInstancePropertyGetter(module, awaiterType, "IsCompleted");
            if (isCompletedGetter is null || !isCompletedGetter.ReturnType.Is(typeof(bool)))
                ThrowNotAwaitable();

            var getResult = FindPublicInstanceMethod(module, awaiterType, "GetResult");
            if (getResult is null)
                ThrowNotAwaitable();

            MethodInfo awaitMethod;
            if (ImplementsInterface(awaiterType, holder.T_ICriticalNotifyCompletion))
            {
                awaitMethod = holder.UnsafeAwaitAwaiter;
            }
            else if (ImplementsInterface(awaiterType, holder.T_INotifyCompletion))
            {
                awaitMethod = holder.AwaitAwaiter;
            }
            else
            {
                ThrowNotAwaitable();
                return;
            }

            var genericAwaitMethod = new GenericInstanceMethod(module.ImportReference(awaitMethod));
            genericAwaitMethod.GenericArguments.Add(module.ImportReference(awaiterType));

            var local = new VariableDefinition(awaiterType);
            il.Body.Variables.Add(local);
            il.Emit(CecilOpCodes.Call, getAwaiter);
            il.Emit(CecilOpCodes.Stloc, local);

            var label = il.Create(CecilOpCodes.Nop);
            var awaiterDefinition = awaiterType.SafeResolve();
            var awaiterIsValueType = awaiterDefinition?.IsValueType ?? awaiterType.IsValueType;
            il.Emit(awaiterIsValueType ? CecilOpCodes.Ldloca : CecilOpCodes.Ldloc, local);
            il.Emit(CecilOpCodes.Call, isCompletedGetter);
            il.Emit(CecilOpCodes.Brtrue, label);

            il.Emit(CecilOpCodes.Ldloc, local);
            il.Emit(CecilOpCodes.Call, genericAwaitMethod);

            il.Append(label);
            il.Emit(awaiterIsValueType ? CecilOpCodes.Ldloca : CecilOpCodes.Ldloc, local);
            il.Emit(CecilOpCodes.Call, getResult);
        }

    }
}
