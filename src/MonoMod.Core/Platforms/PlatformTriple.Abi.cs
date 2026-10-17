using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Core.Platforms
{
    /// <summary>
    /// Flags which are used to inform <see cref="PlatformTriple.GetMethodAbi(MethodBase, MethodAbiFlags)"/> of
    /// information about the method which it cannot determine on its own from just the <see cref="MethodBase"/>.
    /// </summary>
    /// <remarks>
    /// These flags are those determinable only by the <see cref="IRuntime"/> implementation for the current runtime.
    /// </remarks>
    [Flags]
    public enum MethodAbiFlags
    {
        /// <summary>
        /// The method requires a generic context parameter.
        /// </summary>
        HasGenericContext = 1 << 0,
        /// <summary>
        /// The method requires an async continuation parameter.
        /// </summary>
        HasAsyncContinuation = 1 << 1,
    }

    public partial class PlatformTriple
    {
        /// <summary>
        /// Gets the <see cref="MethodAbi"/> for the <paramref name="method"/> variant identified by <paramref name="handle"/>.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="handle"/> MUST be a valid handle corresponding to <paramref name="method"/>.</para>
        /// </remarks>
        /// <param name="method">The <see cref="MethodBase"/> of the method.</param>
        /// <param name="handle">The <see cref="RuntimeMethodHandle"/> of the variant to use, if it is not the default.</param>
        /// <returns>The <see cref="MethodAbi"/> of that method variant.</returns>
        public MethodAbi GetMethodAbi(MethodBase method, RuntimeMethodHandle handle = default)
            => GetMethodAbi(method, Runtime.GetMethodAbiFlags(method, handle));

        /// <summary>
        /// Gets the <see cref="MethodAbi"/> for <paramref name="method"/>, assuming <paramref name="abiFlags"/> are correct.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="abiFlags"/> is usually retrieved from <see cref="IRuntime.GetMethodAbiFlags(MethodBase, RuntimeMethodHandle)"/>.</para>
        /// </remarks>
        /// <param name="method">The method to get the ABi of.</param>
        /// <param name="abiFlags">The <see cref="MethodAbiFlags"/> to assume.</param>
        /// <returns>The <see cref="MethodAbi"/> for <paramref name="method"/>.</returns>
        public MethodAbi GetMethodAbi(MethodBase method, MethodAbiFlags abiFlags)
        {
            method = GetIdentifiable(method);

            var hasGctx = abiFlags.Has(MethodAbiFlags.HasGenericContext);
            var hasContinuation = abiFlags.Has(MethodAbiFlags.HasAsyncContinuation);

            // a non-MethodInfo MethodBase is a ConstructorInfo
            var returnType = method is MethodInfo mi ? mi.ReturnType : typeof(void);

            var args = new List<AbiArgument>();

            var abiOrder = Abi.ArgumentOrder.Span;
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
                        if (Abi.Classify(returnType, true) is TypeClassification.ByReference)
                        {
                            // return type is passed by-reference in the return slot, i.e. we need a
                            // return buffer. We need to synthesize that as a gc-reference, since it may
                            // point into the GC heap.
                            var rbufType = returnType.MakeByRefType();
                            args.Add(new(
                                Kind: SpecialArgumentKind.ReturnBuffer,
                                Type: rbufType));

                            // we then need to update the return type properly
                            if (Abi.ReturnsReturnBuffer)
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

            return new(args.ToArray(), returnType, Abi.ReturnsReturnBuffer);
        }
    }
}
