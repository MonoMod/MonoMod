using MonoMod.Core.Platforms;
using MonoMod.Utils;
using MonoMod.Utils.Cil;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MonoMod.RuntimeDetour.Generics
{
    internal static class SharedDispatchTrampoline
    {
        public readonly struct Result
        {
            public readonly IntPtr Entry;
            // default when no orig
            public readonly GCHandle OrigHandle;
            public readonly object KeepAlive;
            public Result(IntPtr entry, GCHandle origHandle, object keepAlive)
            {
                Entry = entry;
                OrigHandle = origHandle;
                KeepAlive = keepAlive;
            }
        }
        public readonly struct ChainStep
        {
            public readonly Delegate Delegate;
            // default when no orig
            public readonly GCHandle InnerHandle;
            public readonly object KeepAlive;
            public ChainStep(Delegate @delegate, GCHandle innerHandle, object keepAlive)
            {
                Delegate = @delegate;
                InnerHandle = innerHandle;
                KeepAlive = keepAlive;
            }
        }

        private static readonly MethodInfo GCHandle_FromIntPtr = typeof(GCHandle).GetMethod(nameof(GCHandle.FromIntPtr), BindingFlags.Public | BindingFlags.Static, null, [typeof(IntPtr)], null)!;
        private static readonly MethodInfo GCHandle_get_Target = typeof(GCHandle).GetProperty(nameof(GCHandle.Target))!.GetGetMethod()!;

        public static Result Build(MethodBase closedSource, MethodInfo closedTarget, Delegate? orig, GenericContextReader.GenericContextKind kind)
        {
            var abi = PlatformTriple.Current.Abi;

            var hasThis = !closedSource.IsStatic;
            var ctxIsForwarded = GenericHelper.IsContextForwarded(kind);

            var srcParams = closedSource.GetParameters();
            var returnType = (closedSource as MethodInfo)?.ReturnType ?? typeof(void);
            var hasReturnBuffer = GenericHelper.HasReturnBuffer(returnType);
            var returnBufferType = hasReturnBuffer ? returnType.MakeByRefType() : returnType;

            var trampParams = PlatformTriple.BuildAbiArgumentLayout(
                abi.ArgumentOrder.Span, closedSource, srcParams,
                hasThis, hasReturnBuffer, ctxIsForwarded, returnBufferType,
                out var selfIdx, out var returnBufferPos, out _, out var firstUserIdx);

            var retBufIsArg = hasReturnBuffer && returnBufferPos >= 0;
            var newReturnType = retBufIsArg
                ? (abi.ReturnsReturnBuffer ? returnBufferType : typeof(void))
                : returnType;

            var origHandle = default(GCHandle);
            using var dmd = new DynamicMethodDefinition($"SharedGenericDispatch<{closedTarget}>", newReturnType, trampParams);
            try
            {
                var il = new CecilILGenerator(dmd.Definition.Body.GetILProcessor());

                if (retBufIsArg)
                {
                    Ldarg(il, returnBufferPos);
                }

                if (orig is not null)
                {
                    var origParamType = closedTarget.GetParameters()[0].ParameterType;
                    origHandle = GCHandle.Alloc(orig);
                    EmitLoadGCHandleTarget(il, GCHandle.ToIntPtr(origHandle), origParamType);
                }

                if (hasThis)
                {
                    Ldarg(il, selfIdx);
                }

                for (var i = 0; i < srcParams.Length; i++)
                {
                    Ldarg(il, firstUserIdx + i);
                }

                il.Emit(OpCodes.Call, closedTarget);

                if (retBufIsArg)
                {
                    il.Emit(OpCodes.Stobj, returnType);
                    if (abi.ReturnsReturnBuffer)
                    {
                        Ldarg(il, returnBufferPos);
                    }
                }

                il.Emit(OpCodes.Ret);

                // Generated via DMDCecilGenerator because DynamicMethod.MethodHandle throws.
                var generated = DMDCecilGenerator.Generate(dmd);

                RuntimeHelpers.PrepareMethod(generated.MethodHandle);
                var entry = generated.MethodHandle.GetFunctionPointer();

                return new Result(entry, origHandle, generated);
            }
            catch
            {
                if (origHandle.IsAllocated)
                {
                    origHandle.Free();
                }

                throw;
            }
        }

        public static ChainStep BuildManagedChainStep(MethodInfo closedTarget, Delegate? innerOrig, Type origDelegateType)
        {
            var srcParams = origDelegateType.GetMethod("Invoke") ?? throw new InvalidOperationException($"{origDelegateType} has no Invoke method.");
            var invokeParams = srcParams.GetParameters();
            var trampParams = new Type[invokeParams.Length];
            for (var i = 0; i < invokeParams.Length; i++)
            {
                trampParams[i] = invokeParams[i].ParameterType;
            }
            var returnType = srcParams.ReturnType;

            var innerHandle = default(GCHandle);
            using var dmd = new DynamicMethodDefinition($"SharedGenericChainStep<{closedTarget}>", returnType, trampParams);
            try
            {
                var il = new CecilILGenerator(dmd.Definition.Body.GetILProcessor());

                if (innerOrig is not null)
                {
                    var origParamType = closedTarget.GetParameters()[0].ParameterType;
                    innerHandle = GCHandle.Alloc(innerOrig);
                    EmitLoadGCHandleTarget(il, GCHandle.ToIntPtr(innerHandle), origParamType);
                }

                for (var i = 0; i < trampParams.Length; i++)
                {
                    Ldarg(il, i);
                }

                il.Emit(OpCodes.Call, closedTarget);
                il.Emit(OpCodes.Ret);

                var generated = DMDCecilGenerator.Generate(dmd);
                RuntimeHelpers.PrepareMethod(generated.MethodHandle);
                var del = generated.CreateDelegate(origDelegateType);

                return new ChainStep(del, innerHandle, generated);
            }
            catch
            {
                if (innerHandle.IsAllocated)
                {
                    innerHandle.Free();
                }

                throw;
            }
        }

        private static void EmitLoadGCHandleTarget(ILGeneratorShim il, IntPtr handlePtr, Type castTo)
        {
            if (IntPtr.Size == 8)
            {
                il.Emit(OpCodes.Ldc_I8, (long)handlePtr);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, (int)handlePtr);
            }

            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Call, GCHandle_FromIntPtr);
            var loc = il.DeclareLocal(typeof(GCHandle));
            il.Emit(OpCodes.Stloc, loc);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Call, GCHandle_get_Target);
            il.Emit(OpCodes.Castclass, castTo);
        }

        private static void Ldarg(ILGeneratorShim il, int index)
        {
            switch (index)
            {
                case 0:
                    il.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    if (index <= byte.MaxValue)
                    {
                        il.Emit(OpCodes.Ldarg_S, (byte)index);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg, checked((short)index));
                    }
                    break;
            }
        }
    }
}
