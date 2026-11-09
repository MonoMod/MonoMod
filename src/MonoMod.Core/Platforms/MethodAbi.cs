using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// A description of a method argument at the ABI level.
    /// </summary>
    /// <param name="Kind">The kind of argument.</param>
    /// <param name="Type">The type of the argument.</param>
    public readonly record struct AbiArgument(
        SpecialArgumentKind Kind,
        Type Type);

    /// <summary>
    /// A method's ABI signature.
    /// </summary>
    public sealed class MethodAbi
    {
        /// <summary>
        /// The ABI arguments taken by the method.
        /// </summary>
        public ReadOnlyMemory<AbiArgument> Arguments { get; }
        /// <summary>
        /// The ABI return type of the method. If the method returns nothing, <c><see langword="typeof"/>(<see langword="void"/>)</c>.
        /// </summary>
        public Type AbiReturn { get; }

        /// <summary>
        /// The index of this method's <see langword="this"/> argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int ThisArgument { get; } = -1;
        /// <summary>
        /// The index of this method's return buffer argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int ReturnBufferArgument { get; } = -1;
        /// <summary>
        /// The index of this method's generic context argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int GenericContextArgument { get; } = -1;
        /// <summary>
        /// The index of this method's async continuation argument, or <c>-1</c> if it doesn't have one.
        /// </summary>
        public int AsyncContinuationArgument { get; } = -1;

        /// <summary>
        /// The index of the first user argument, or <c>-1</c> if there are no user arguments.
        /// </summary>
        public int FirstUserArgument { get; } = -1;

        /// <summary>
        /// The number of user arguments in this method ABI.
        /// </summary>
        public int UserArgumentCount { get; }

        /// <summary>
        /// <see langword="true"/> if the ABI returns the return buffer parameter.
        /// </summary>
        public bool AbiReturnIsReturnBuffer { get; }

        /// <summary>
        /// Constructs a <see cref="MethodAbi"/>.
        /// </summary>
        /// <param name="arguments">The <see cref="AbiArgument"/>s of the method's arguments.</param>
        /// <param name="abiReturn">The ABI return type for the method.</param>
        /// <param name="returnReturnBuffer">Whether the ABI requires the method to return the return buffer pointer.</param>
        public MethodAbi(ReadOnlyMemory<AbiArgument> arguments, Type abiReturn, bool returnReturnBuffer)
        {
            Helpers.ThrowIfNull(abiReturn);

            Arguments = arguments;
            AbiReturn = abiReturn;

            Type? argRetBufType = null;
            var argSpan = arguments.Span;
            for (var i = 0; i < argSpan.Length; i++)
            {
                var arg = argSpan[i];
                switch (arg.Kind)
                {
                    case SpecialArgumentKind.ThisPointer:
                        Helpers.Assert(ThisArgument == -1);
                        ThisArgument = i;
                        break;
                    case SpecialArgumentKind.ReturnBuffer:
                        Helpers.Assert(ReturnBufferArgument == -1);
                        ReturnBufferArgument = i;
                        argRetBufType = arg.Type;
                        break;
                    case SpecialArgumentKind.GenericContext:
                        Helpers.Assert(GenericContextArgument == -1);
                        GenericContextArgument = i;
                        break;
                    case SpecialArgumentKind.AsyncContinuation:
                        Helpers.Assert(AsyncContinuationArgument == -1);
                        AsyncContinuationArgument = i;
                        break;

                    case SpecialArgumentKind.UserArguments:
                        if (FirstUserArgument == -1)
                        {
                            FirstUserArgument = i;
                            UserArgumentCount = 1;
                        }
                        else
                        {
                            Helpers.Assert(UserArgumentCount == i - FirstUserArgument);
                            UserArgumentCount++;
                        }
                        break;

                    default:
                        throw new NotImplementedException($"SpecialArgumentKind.{arg.Kind}");
                }
            }

            AbiReturnIsReturnBuffer = returnReturnBuffer && ReturnBufferArgument >= 0;

            if (AbiReturnIsReturnBuffer)
            {
                Helpers.DAssert(argRetBufType is not null);
                Helpers.Assert(argRetBufType == abiReturn);
            }
        }

        /// <summary>
        /// Gets the <see cref="MethodAbi"/> for <paramref name="method"/>, assuming <paramref name="abiFlags"/> are correct.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="abiFlags"/> is usually retrieved from <see cref="IRuntime.GetMethodAbiFlags(MethodAbiUsage, MethodBase, RuntimeMethodHandle)"/>.</para>
        /// </remarks>
        /// <param name="abi">The <see cref="Abi"/> to use.</param>
        /// <param name="method">The method to get the ABi of.</param>
        /// <param name="abiFlags">The <see cref="MethodAbiFlags"/> to assume.</param>
        /// <returns>The <see cref="MethodAbi"/> for <paramref name="method"/>.</returns>
        public static MethodAbi GetMethodAbi(Abi abi, MethodBase method, MethodAbiFlags abiFlags)
        {
            Helpers.ThrowIfArgumentNull(method);

            var hasGctx = abiFlags.Has(MethodAbiFlags.HasGenericContext);
            var hasContinuation = abiFlags.Has(MethodAbiFlags.HasAsyncContinuation);

            // a non-MethodInfo MethodBase is a ConstructorInfo
            var returnType = method is MethodInfo mi ? mi.ReturnType : typeof(void);

            if (hasContinuation)
            {
                // this is the runtime-async variant of an async method. Therefore, we need to unwrap the declared return type task
                // result into the returnType variable.
                Helpers.Assert(RuntimeAsync.IsSupported);
                // TODO: support getting the MethodAbi for async variants even when the current runtime doesn't support it

                if (returnType.IsGenericType)
                {
                    var gtd = returnType.GetGenericTypeDefinition();
                    Helpers.Assert(gtd == RuntimeAsync.TaskT || gtd == RuntimeAsync.ValueTaskT);
                    returnType = returnType.GetGenericArguments()[0];
                }
                else
                {
                    Helpers.Assert(returnType == RuntimeAsync.Task || returnType == RuntimeAsync.ValueTask);
                    returnType = typeof(void);
                }
            }

            var args = new List<AbiArgument>();

            var abiOrder = abi.ArgumentOrder.Span;
            for (var i = 0; i < abiOrder.Length; i++)
            {
                var kind = abiOrder[i];
                switch (kind)
                {
                    case SpecialArgumentKind.ThisPointer:
                        if (!method.IsStatic)
                        {
                            // the method bas an ABI-this parameter
                            args.Add(new(
                                Kind: SpecialArgumentKind.ThisPointer,
                                Type: method.GetThisParamType()));
                        }
                        break;
                    case SpecialArgumentKind.ReturnBuffer:
                        // we know we need to put the return buffer in the argument list somewhere
                        if (abi.Classify(returnType, true) is TypeClassification.ByReference)
                        {
                            // return type is passed by-reference in the return slot, i.e. we need a
                            // return buffer. We need to synthesize that as a gc-reference, since it may
                            // point into the GC heap.
                            var rbufType = returnType.MakeByRefType();
                            args.Add(new(
                                Kind: SpecialArgumentKind.ReturnBuffer,
                                Type: rbufType));

                            // we then need to update the return type properly
                            if (abi.ReturnsReturnBuffer)
                            {
                                // the return slot gets the return buffer type
                                returnType = rbufType;
                            }
                            else
                            {
                                // don't need to return the retbuf, return void instead
                                returnType = typeof(void);
                            }
                        }
                        break;
                    case SpecialArgumentKind.GenericContext:
                        if (hasGctx)
                        {
                            // on no current runtimes is the generic context a GC-managed object
                            // though in theory it could become so at some point. so:
                            // TODO: ask the runtime somehow how we should represent GenericContext in ABI
                            args.Add(new(
                                Kind: SpecialArgumentKind.GenericContext,
                                Type: typeof(void*)));
                        }
                        break;
                    case SpecialArgumentKind.AsyncContinuation:
                        if (hasContinuation)
                        {
                            // on all current runtimes supporting runtime-async, the continuation
                            // object is GC-managed, but it could in theory not be sometimes. So, like above:
                            // TODO: ask the runtime somehow how we should represent AsyncContinuation in ABI
                            args.Add(new(
                                Kind: SpecialArgumentKind.AsyncContinuation,
                                Type: typeof(object)));
                        }
                        break;
                    case SpecialArgumentKind.UserArguments:
                        // user arguments just get forwarded directly to the ABI level
                        foreach (var arg in method.GetParameters())
                        {
                            // TODO: when we do proper generics support, we need to substitute arguments
                            // which correspond to shared generic parameters with a type corresponding to
                            // that shared handle
                            args.Add(new(
                                Kind: SpecialArgumentKind.UserArguments,
                                Type: arg.ParameterType));
                        }
                        break;

                    default:
                        throw new NotImplementedException($"SpecialArgumentKind.{kind}");
                }
            }

            return new(args.ToArray(), returnType, abi.ReturnsReturnBuffer);
        }
    }
}
