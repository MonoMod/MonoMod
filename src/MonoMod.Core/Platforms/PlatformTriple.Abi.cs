using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Reflection;

namespace MonoMod.Core.Platforms
{
    public partial class PlatformTriple
    {
        /// <summary>
        /// Gets the <see cref="MethodAbi"/> for the <paramref name="method"/> variant identified by <paramref name="handle"/>.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="handle"/> MUST be a valid handle corresponding to <paramref name="method"/>.</para>
        /// </remarks>
        /// <param name="usage">The <see cref="MethodAbiUsage"/> indicating which method usage to consider.</param>
        /// <param name="method">The <see cref="MethodBase"/> of the method.</param>
        /// <param name="handle">The <see cref="RuntimeMethodHandle"/> of the variant to use, if it is not the default.</param>
        /// <returns>The <see cref="MethodAbi"/> of that method variant.</returns>
        /// <seealso cref="IRuntime.GetMethodAbiFlags(MethodAbiUsage, MethodBase, RuntimeMethodHandle)"/>
        /// <seealso cref="GetMethodAbi(MethodBase, MethodAbiFlags)"/>
        public MethodAbi GetMethodAbi(MethodAbiUsage usage, MethodBase method, RuntimeMethodHandle handle = default)
            => GetMethodAbi(method, Runtime.GetMethodAbiFlags(usage, method, handle));

        /// <summary>
        /// Gets the <see cref="MethodAbi"/> for <paramref name="method"/>, assuming <paramref name="abiFlags"/> are correct.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="abiFlags"/> is usually retrieved from <see cref="IRuntime.GetMethodAbiFlags(MethodAbiUsage, MethodBase, RuntimeMethodHandle)"/>.</para>
        /// </remarks>
        /// <param name="method">The method to get the ABi of.</param>
        /// <param name="abiFlags">The <see cref="MethodAbiFlags"/> to assume.</param>
        /// <returns>The <see cref="MethodAbi"/> for <paramref name="method"/>.</returns>
        /// <seealso cref="GetMethodAbi(MethodAbiUsage, MethodBase, RuntimeMethodHandle)"/>
        public MethodAbi GetMethodAbi(MethodBase method, MethodAbiFlags abiFlags)
            => MethodAbi.GetMethodAbi(Abi, GetIdentifiable(method), abiFlags);

        /// <summary>
        /// Gets the 'real detour target' when detouring <paramref name="src"/> to <paramref name="dst"/>.
        /// </summary>
        /// <remarks>
        /// This will return an ABI fixup method instead of <paramref name="dst"/> when needed, automatically
        /// generating them when needed. When this is needed depends on the argument order in the ABI.
        /// </remarks>
        /// <param name="src">The method being detoured from.</param>
        /// <param name="dst">The method being detoured to.</param>
        /// <param name="argumentMap">The <see cref="AbiArgumentMap"/> specifying how to map arguments from <paramref name="src"/>
        /// onto the arguments of <paramref name="dst"/>.</param>
        /// <returns>The method to detour to instead of <paramref name="dst"/>.</returns>
        public MethodBase GetRealDetourTarget(MethodBase src, MethodBase dst, AbiArgumentMap argumentMap)
        {
            Helpers.ThrowIfArgumentNull(src);
            Helpers.ThrowIfArgumentNull(dst);
            Helpers.ThrowIfArgumentNull(argumentMap);

            src = GetIdentifiable(src);
            dst = GetIdentifiable(dst);

            var srcAbi = GetMethodAbi(MethodAbiUsage.RealMethodBody, src);
            var dstAbi = GetMethodAbi(MethodAbiUsage.NormalEntry, dst);

            var argOrder = new int[dstAbi.Arguments.Length];
            var result = argumentMap.MapArguments(srcAbi, dstAbi, argOrder);
            result.ThrowIfInvalid();
            if (result.Has(AbiArgumentMapResult.SourceHasUnmappedUserArguments))
            {
                MMDbgLog.Warning($"Mapping from src={src} to dst={dst} has unused user arguments in src");
            }
            else if (result.Has(AbiArgumentMapResult.SourceHasUnmappedArguments))
            {
                MMDbgLog.Warning($"Mapping from src={src} to dst={dst} has unused ABI arguments in src");
            }
            if (result.Has(AbiArgumentMapResult.MappingIsIdentity))
            {
                // the mapping is the identity map, we can just use dst as the target
                return dst;
            }

            var srcAbiArgs = srcAbi.Arguments.Span;
            var dstAbiArgs = dstAbi.Arguments.Span;

            var argumentTypes = new Type[srcAbi.Arguments.Length];
            for (var i = 0; i < argumentTypes.Length; i++)
            {
                argumentTypes[i] = srcAbiArgs[i].Type;
            }

            // this is a non-identity mapping, we need to do real work
            using var dmd = new DynamicMethodDefinition(
                DebugFormatter.Format($"Glue:AbiFixup<{src},{dst}>"),
                srcAbi.AbiReturn, argumentTypes
            );
            // TODO: make DMD apply attributes to the generated DynamicMethod, when possible
            dmd.Definition!.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining |
                (Mono.Cecil.MethodImplAttributes)(int)MethodImplOptionsEx.AggressiveOptimization;

            var il = dmd.GetILProcessor();

            // if we have a retbuf, grab it first so the IL is correct
            if (dstAbi.ReturnBufferArgument >= 0)
            {
                il.Emit(OpCodes.Ldarg, argOrder[dstAbi.ReturnBufferArgument]);
            }

            // if dst takes a this argument, load it before the user arguments
            if (dstAbi.ThisArgument >= 0)
            {
                il.Emit(OpCodes.Ldarg, argOrder[dstAbi.ThisArgument]);
            }

            // then load all of the user arguments in-order
            for (var i = 0; i < argOrder.Length; i++)
            {
                var dstArg = dstAbiArgs[i];
                if (dstArg.Kind is not SpecialArgumentKind.UserArguments)
                    continue;

                il.Emit(OpCodes.Ldarg, argOrder[i]);
            }

            // TODO: non-UserArguments

            // then emit the call
            var mr = il.Body.Method.Module.ImportReference(dst);
            il.Emit(OpCodes.Call, mr);

            // if we have a retbuf, store that
            if (dstAbi.ReturnBufferArgument >= 0)
            {
                il.Emit(OpCodes.Stobj, mr.ReturnType);
            }

            // if we're support to return the retbuf, do so
            if (srcAbi.AbiReturnIsReturnBuffer)
            {
                il.Emit(OpCodes.Ldarg, argOrder[dstAbi.ReturnBufferArgument]);
            }

            // then we're done
            il.Emit(OpCodes.Ret);

            return dmd.Generate();
        }
    }
}
