using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Cil
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class GetFastDelegateInvokersArrayAttribute : Attribute
    {
        public int MaxParams { get; }
        public GetFastDelegateInvokersArrayAttribute(int maxParams)
            => MaxParams = maxParams;
    }

    public static partial class FastDelegateInvokers
    {
        private static readonly (MethodInfo, Type)[] invokers = GetInvokers();

        private const int MaxFastInvokerParams = 16;

        [GetFastDelegateInvokersArray(MaxFastInvokerParams)]
        private static partial (MethodInfo, Type)[] GetInvokers();

        private static (MethodInfo Invoker, Type Delegate)? TryGetInvokerForSig(MethodSignature sig)
        {
            // if the signature doesn't take any arguments, we don't need an invoker in the first place
            if (sig.ParameterCount == 0)
                return null;

            // if the signature takes more parameters than our max, we don't have a pregenerated invoker for it
            if (sig.ParameterCount > MaxFastInvokerParams)
                return null;

            // we want to construct an index to look up an invoker
            // this index is structured as follows (low to high bits)
            //     xyzzzzz...
            // x: has non-void return
            // y: first param is byref
            // z: number of parameters AFTER the first

            Helpers.DAssert(sig.FirstParameter is not null);

            // make sure that the return type is not byref or byreflike
            if (sig.ReturnType.IsByRef || sig.ReturnType.IsByRefLike())
                return null;
            // make sure that the first parameter is not byreflike
            if (sig.FirstParameter.IsByRefLike())
                return null;
            // make sure that the other parameters are not byref or byreflike
            if (sig.Parameters.Skip(1).Any(t => t.IsByRef || t.IsByRefLike()))
                return null;

            var index = 0;
            index |= sig.ReturnType != typeof(void) ? 0b01 : 0b00;
            index |= sig.FirstParameter.IsByRef ? 0b10 : 0b00;
            index |= (sig.ParameterCount - 1) << 2;

            var (invoker, del) = invokers[index];

            var typeParams = new Type[sig.ParameterCount + (index & 1)];
            // first param is always return type, if present
            var i = 0;
            if ((index & 1) != 0)
                typeParams[i++] = sig.ReturnType;
            foreach (var p in sig.Parameters)
            {
                var t = p;
                if (t.IsByRef)
                    t = t.GetElementType()!;
                typeParams[i++] = t;
            }
            Helpers.Assert(i == typeParams.Length);

            return (invoker.MakeGenericMethod(typeParams), del.MakeGenericType(typeParams));
        }

        private static readonly ConditionalWeakTable<Type, Tuple<MethodInfo?, Type>> invokerCache = new();
        public static (MethodInfo Invoker, Type Delegate)? GetDelegateInvoker(Type delegateType)
        {
            Helpers.ThrowIfArgumentNull(delegateType);
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
                throw new ArgumentException("Argument not a delegate type", nameof(delegateType));

            var tuple = invokerCache.GetValue(delegateType, static delegateType =>
            {

                var delInvoke = delegateType.GetMethod("Invoke")!;
                var sig = MethodSignature.ForMethod(delInvoke, ignoreThis: true);

                if (sig.ParameterCount == 0)
                {
                    return new(null, delegateType);
                }

                var builtinInvoker = TryGetInvokerForSig(sig);
                if (builtinInvoker is { } p)
                {
                    return new(p.Invoker, p.Delegate);
                }

                var argTypes = new Type[sig.ParameterCount + 1];
                var i = 0;
                foreach (var param in sig.Parameters)
                {
                    argTypes[i++] = param;
                }
                argTypes[sig.ParameterCount] = delegateType;

                using (var dmdInvoke = new DynamicMethodDefinition(
                    $"MMIL:Invoke<{delInvoke.DeclaringType?.FullName}>",
                    delInvoke.ReturnType, argTypes
                ))
                {
                    var il = dmdInvoke.GetILProcessor();

                    // Load the delegate reference first.
                    il.Emit(OpCodes.Ldarg, sig.ParameterCount);

                    // Load the rest of the args
                    for (i = 0; i < sig.ParameterCount; i++)
                        il.Emit(OpCodes.Ldarg, i);

                    // Invoke the delegate and return its result.
                    il.Emit(OpCodes.Callvirt, delInvoke);
                    il.Emit(OpCodes.Ret);

                    var invoker = dmdInvoke.Generate();
                    return new(invoker, delegateType);
                }
            });

            if (tuple.Item1 is null)
                return null;
            return (tuple.Item1, tuple.Item2);
        }
    }
}
