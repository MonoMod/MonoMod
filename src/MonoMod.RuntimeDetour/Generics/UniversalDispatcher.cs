using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;
using MonoMod.Core.Platforms;

namespace MonoMod.RuntimeDetour.Generics
{
    // Builds the per-body managed dispatcher the shared native body is detoured to.
    // The dispatcher reads the generic context (from its argument, or from this for the ThisObject kind),
    // resolves the target entry, then forwards via calli
    // On Mono the context arrives via the capture stub and is read but NOT forwarded.
    internal static class UniversalDispatcher
    {
        public readonly struct Result
        {
            public readonly IntPtr Entry;
            public readonly object KeepAlive;
            // For Mono: the integer-argument-register index the capture stub must move the generic context into;
            // -1 when no capture stub is needed (CoreCLR/Framework, or the ThisObject kind).
            public readonly int ContextArgRegisterIndex;
            public Result(IntPtr entry, object keepAlive, int contextArgRegisterIndex)
            {
                Entry = entry;
                KeepAlive = keepAlive;
                ContextArgRegisterIndex = contextArgRegisterIndex;
            }
        }

        private static readonly MethodInfo resolveForDispatch = ((Func<int, IntPtr, IntPtr>)SharedBodyDispatcher.ResolveForDispatch).Method;
        private static readonly MethodInfo resolveForDispatchThis = ((Func<int, object, IntPtr>)SharedBodyDispatcher.ResolveForDispatchThis).Method;
        private static readonly ConstructorInfo invalidOpCtor = typeof(InvalidOperationException).GetConstructor([typeof(string)])!;

        public static Result Build(MethodBase closedSource, GenericContextReader.GenericContextKind kind, int bodyId)
        {
            Helpers.ThrowIfArgumentNull(closedSource);

            var abi = PlatformTriple.Current.Abi;

            var hasThis = !closedSource.IsStatic;
            var explicitCtx = kind != GenericContextReader.GenericContextKind.ThisObject;
            var isMono = PlatformDetection.Runtime == RuntimeKind.Mono;
            // The context is forwarded (as a real argument) only on CoreCLR/Framework.
            // On Mono it came from r10 and is consumed here.
            var ctxIsForwarded = GenericHelper.IsContextForwarded(kind);

            var returnType = (closedSource as MethodInfo)?.ReturnType ?? typeof(void);
            var hasReturnBuffer = GenericHelper.HasReturnBuffer(returnType);
            var returnBufferType = hasReturnBuffer ? returnType.MakeByRefType() : returnType;

            // On Mono the ABI argument order has no generic-context slot (it's in r10);
            // append one so we have an argument to read it from. Elsewhere the ABI order already places it.
            ReadOnlySpan<SpecialArgumentKind> order;
            if (explicitCtx && isMono)
            {
                var baseOrder = abi.ArgumentOrder.Span;
                var appended = new SpecialArgumentKind[baseOrder.Length + 1];
                baseOrder.CopyTo(appended);
                appended[baseOrder.Length] = SpecialArgumentKind.GenericContext;
                order = appended;
            }
            else
            {
                order = abi.ArgumentOrder.Span;
            }

            var parameters = closedSource.GetParameters();
            var paramTypes = PlatformTriple.BuildAbiArgumentLayout(
                order, closedSource, parameters,
                hasThis, hasReturnBuffer, explicitCtx, returnBufferType,
                out var thisPos, out var returnBufferPos, out var genCtxPos, out _,
                Canonical);

            var retBufIsArg = hasReturnBuffer && returnBufferPos >= 0;
            var newReturnType = retBufIsArg
                ? (abi.ReturnsReturnBuffer ? returnBufferType : typeof(void))
                : returnType;

            using var dmd = new DynamicMethodDefinition($"UniversalGenericDispatch<{closedSource}>", newReturnType, paramTypes);
            var module = dmd.Module;
            var il = dmd.GetILProcessor();
            var dmParams = dmd.Definition.Parameters;

            var entryLocal = new VariableDefinition(module.ImportReference(typeof(IntPtr)));
            dmd.Definition.Body.Variables.Add(entryLocal);

            // entry = Resolve...(bodyId, context | this)
            il.Emit(OpCodes.Ldc_I4, bodyId);
            if (explicitCtx)
            {
                il.Emit(OpCodes.Ldarg, dmParams[genCtxPos]);
                il.Emit(OpCodes.Call, module.ImportReference(resolveForDispatch));
            }
            else
            {
                il.Emit(OpCodes.Ldarg, dmParams[thisPos]);
                il.Emit(OpCodes.Call, module.ImportReference(resolveForDispatchThis));
            }
            il.Emit(OpCodes.Stloc, entryLocal);

            var notNull = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Brtrue, notNull);
            il.Emit(OpCodes.Ldstr, $"Generic dispatch for {closedSource} resolved to a null target (generic-context read or fallback build failed).");
            il.Emit(OpCodes.Newobj, module.ImportReference(invalidOpCtor));
            il.Emit(OpCodes.Throw);
            il.Append(notNull);

            var callSite = new Mono.Cecil.CallSite(module.ImportReference(newReturnType));
            for (var i = 0; i < paramTypes.Length; i++)
            {
                if (i == genCtxPos && !ctxIsForwarded)
                {
                    continue;
                }
                il.Emit(OpCodes.Ldarg, dmParams[i]);
                callSite.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(paramTypes[i])));
            }
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Calli, callSite);
            il.Emit(OpCodes.Ret);

            var generated = DMDCecilGenerator.Generate(dmd);
            RuntimeHelpers.PrepareMethod(generated.MethodHandle);
            var entry = generated.MethodHandle.GetFunctionPointer();

            var ctxRegIndex = -1;
            if (explicitCtx && isMono)
            {
                ctxRegIndex = MonoContextRegisterCalibrator.TryFindContextArgRegister(paramTypes, genCtxPos) ?? CountIntegerArgRegistersBefore(paramTypes, genCtxPos);
            }

            return new Result(entry, generated, ctxRegIndex);
        }

        // Fallback approximation when calibration is unavailable: count of integer argument registers consumed by the
        // slots preceding the generic context; FP args are assumed to use FP registers and are skipped.
        // TODO: Find a proper not hacky implementation
        private static int CountIntegerArgRegistersBefore(Type[] paramTypes, int genCtxPos)
        {
            var count = 0;
            for (var i = 0; i < genCtxPos; i++)
            {
                var t = paramTypes[i];
                if (t == typeof(float) || t == typeof(double))
                {
                    continue;
                }
                count++;
            }
            return count;
        }

        private static Type Canonical(Type t) => t.IsByRef || t.IsValueType ? t : typeof(object);

        // Build the orig delegate as a direct call into the original shared body with this instantiation's fixed generic context,
        // instead of cloning the concrete method's IL.
        public static (Delegate Orig, object KeepAlive) BuildOrigViaBody(MethodBase closedSource, IntPtr origEntrypoint, IntPtr fixedContext, Type origDelType)
        {
            Helpers.ThrowIfArgumentNull(closedSource);
            var abi = PlatformTriple.Current.Abi;

            var hasThis = !closedSource.IsStatic;
            var srcParams = closedSource.GetParameters();
            var returnType = (closedSource as MethodInfo)?.ReturnType ?? typeof(void);
            var hasReturnBuffer = GenericHelper.HasReturnBuffer(returnType);
            var returnBufferType = hasReturnBuffer ? returnType.MakeByRefType() : returnType;

            var calliParams = PlatformTriple.BuildAbiArgumentLayout(
                abi.ArgumentOrder.Span, closedSource, srcParams,
                hasThis, hasReturnBuffer, true, returnBufferType,
                out var thisPos, out var returnBufferPos, out var genCtxPos, out var userArgsOffset);

            var retBufIsArg = hasReturnBuffer && returnBufferPos >= 0;
            var newReturnType = retBufIsArg
                ? (abi.ReturnsReturnBuffer ? returnBufferType : typeof(void))
                : returnType;

            var invoke = origDelType.GetMethod("Invoke") ?? throw new InvalidOperationException($"{origDelType} has no Invoke method.");
            var managedParams = Array.ConvertAll(invoke.GetParameters(), p => p.ParameterType);
            var managedReturn = invoke.ReturnType;
            var firstUserManaged = hasThis ? 1 : 0;

            using var dmd = new DynamicMethodDefinition($"SharedGenericOrigViaBody<{closedSource}>", managedReturn, managedParams);
            var module = dmd.Module;
            var il = dmd.GetILProcessor();
            var dmParams = dmd.Definition.Parameters;

            VariableDefinition? retLocal = null;
            if (retBufIsArg)
            {
                retLocal = new VariableDefinition(module.ImportReference(returnType));
                dmd.Definition.Body.Variables.Add(retLocal);
            }

            var callSite = new Mono.Cecil.CallSite(module.ImportReference(newReturnType));
            for (var i = 0; i < calliParams.Length; i++)
            {
                if (i == thisPos)
                {
                    il.Emit(OpCodes.Ldarg, dmParams[0]);
                }
                else if (i == returnBufferPos)
                {
                    il.Emit(OpCodes.Ldloca, retLocal!);
                }
                else if (i == genCtxPos)
                {
                    EmitLdcIntPtr(il, fixedContext);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, dmParams[firstUserManaged + (i - userArgsOffset)]);
                }
                callSite.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(calliParams[i])));
            }

            EmitLdcIntPtr(il, origEntrypoint);
            il.Emit(OpCodes.Calli, callSite);

            if (retBufIsArg)
            {
                if (abi.ReturnsReturnBuffer)
                {
                    il.Emit(OpCodes.Pop);
                }
                il.Emit(OpCodes.Ldloc, retLocal!);
            }
            il.Emit(OpCodes.Ret);

            var generated = DMDCecilGenerator.Generate(dmd);
            RuntimeHelpers.PrepareMethod(generated.MethodHandle);
            return (generated.CreateDelegate(origDelType), generated);
        }

        private static void EmitLdcIntPtr(Mono.Cecil.Cil.ILProcessor il, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                il.Emit(OpCodes.Ldc_I8, (long)value);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, (int)value);
            }
            il.Emit(OpCodes.Conv_I);
        }
    }
}
