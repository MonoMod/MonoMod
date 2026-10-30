using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.Logs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CecilOpCodes = Mono.Cecil.Cil.OpCodes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilMethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

#if NET9_0_OR_GREATER
using System.Threading;
using System.Reflection.Emit;
#endif

namespace MonoMod.Utils.DMDGenerators
{
    internal static class DelegateTypeGenerator
    {
        private sealed class DelegateTypeCacheKey : IEquatable<DelegateTypeCacheKey>, IDebugFormattable
        {
            [Flags]
            private enum ParamFlags
            {
                ByRef = 1 << 0,
                ByRefLike = 1 << 1,
            }

            private const int NumParamFlags = 2;
            private const ParamFlags ReturnIsVoid = (ParamFlags)(1 << NumParamFlags);

            private readonly int paramCount;
            private readonly int generics;
            private readonly ParamFlags returnFlags;
            private readonly uint[] paramFlags;
            // byRefLikes is a compressed list of the types for byreflike parameters
            // first is return type (if its byreflike), then the first byreflike param,
            // then the second byreflike param, etc
            private readonly Type[] byRefLikes;
            private readonly bool hasByRefs;
            private readonly bool hasNonFirstByRefs;

            private DelegateTypeCacheKey(int paramCount, int generics, ParamFlags returnFlags, uint[] paramFlags, Type[] byRefLikes, bool hasByRefs, bool hasNonFirstByRefs)
            {
                this.paramCount = paramCount;
                this.generics = generics;
                this.returnFlags = returnFlags;
                this.paramFlags = paramFlags;
                this.byRefLikes = byRefLikes;
                this.hasByRefs = hasByRefs;
                this.hasNonFirstByRefs = hasNonFirstByRefs;
            }

            public static DelegateTypeCacheKey Create(MethodSignature sig)
            {
                var byRefLikes = new List<Type>(sig.ParameterCount);
                var generics = 0;
                var hasByRefs = false;

                ParamFlags PushType(Type type)
                {
                    if (type == typeof(void))
                    {
                        return ReturnIsVoid;
                    }

                    ParamFlags result = 0;
                    if (type.IsByRef)
                    {
                        result |= ParamFlags.ByRef;
                        // type is byref, unwrap it so we can check its element type
                        type = type.GetElementType()!;
                        hasByRefs = true;
                    }

                    if (type.IsByRefLike() || type.IsPointer)
                    {
                        result |= ParamFlags.ByRefLike;
                        byRefLikes.Add(type);
                    }
                    else
                    {
                        generics++;
                    }

                    return result;
                }

                var paramFlags = new List<uint>((sig.ParameterCount * NumParamFlags + 31) / 32);
                uint curParamFlags = 0;
                var curParamFlagsOffs = 0;
                var paramCount = 0;

                var returnFlags = PushType(sig.ReturnType);
                var hasNonFirstByRefs = hasByRefs;
                foreach (var paramType in sig.Parameters)
                {
                    paramCount++;
                    var pFlags = PushType(paramType);
                    if (paramCount != 1)
                    {
                        hasNonFirstByRefs |= pFlags.Has(ParamFlags.ByRef);
                    }

                    var bitOffs = curParamFlagsOffs;
                    curParamFlagsOffs += NumParamFlags;
                    if (curParamFlagsOffs > 32)
                    {
                        paramFlags.Add(curParamFlags);
                        curParamFlags = 0;
                        bitOffs = 0;
                        curParamFlagsOffs = NumParamFlags;
                    }
                    curParamFlags |= (uint)pFlags << bitOffs;
                }

                if (curParamFlagsOffs > 0)
                {
                    paramFlags.Add(curParamFlags);
                }

                return new(paramCount, generics, returnFlags, paramFlags.ToArray(), byRefLikes.ToArray(), hasByRefs, hasNonFirstByRefs);
            }

            public override bool Equals(object? obj) => obj is DelegateTypeCacheKey key && Equals(key);
            public bool Equals(DelegateTypeCacheKey? other)
            {
                if (other is null) return false;

                if (paramCount != other.paramCount) return false;
                if (returnFlags != other.returnFlags) return false;
                if (!paramFlags.SequenceEqual(other.paramFlags)) return false;
                if (!byRefLikes.SequenceEqual(other.byRefLikes)) return false;
                return true;
            }

            public override int GetHashCode()
            {
                var hc = new HashCode();
                hc.Add(paramCount);
                hc.Add(returnFlags);
                foreach (var i in paramFlags)
                {
                    hc.Add(i);
                }
                foreach (var ty in byRefLikes)
                {
                    hc.Add(ty);
                }
                return hc.ToHashCode();
            }

            public override string ToString()
            {
                return DebugFormatter.Format($"{this}");
            }

            public bool TryFormatInto(Span<char> span, out int wrote)
            {
                wrote = 0;
                if (!DebugFormatter.Into(span.Slice(wrote), out var w, $"F{paramCount}->{(int)returnFlags}_"))
                    return false;
                wrote += w;
                foreach (var el in paramFlags)
                {
                    if (!DebugFormatter.Into(span.Slice(wrote), out w, $"{el:x8}"))
                        return false;
                    wrote += w;
                }
                if (!DebugFormatter.Into(span.Slice(wrote), out w, $"[["))
                    return false;
                wrote += w;
                foreach (var ty in byRefLikes)
                {
                    if (!DebugFormatter.Into(span.Slice(wrote), out w, $"{ty},"))
                        return false;
                    wrote += w;
                }
                if (!DebugFormatter.Into(span.Slice(wrote), out w, $"]]"))
                    return false;
                wrote += w;
                if (generics > 0)
                {
                    if (!DebugFormatter.Into(span.Slice(wrote), out w, $"`{generics}"))
                        return false;
                    wrote += w;
                }
                return true;
            }

            public bool HasByRefs => hasByRefs;
            public bool HasNonFirstByRefs => hasNonFirstByRefs;
            public bool HasByRefLikes => byRefLikes.Length > 0;
            public bool ReturnsVoid => returnFlags == ReturnIsVoid;
            public int NumGenericParams => generics;
            public int NumParams => paramCount;

            private ParamFlags GetParamFlags(int param)
            {
                const int paramsPerUint = 32 / NumParamFlags;
                var flagsHolder = paramFlags[param / paramsPerUint];
                var flags = flagsHolder >> (NumParamFlags * (param % paramsPerUint)) & ((1 << NumParamFlags) - 1);
                return (ParamFlags)flags;
            }

            public Type GetReturnType(Type? generic, ref int genericIndex, ref int byreflikeIndex)
            {
                var returnFlags = this.returnFlags;
                if (returnFlags == ReturnIsVoid)
                {
                    return typeof(void);
                }

                return GetParamTypeCore(returnFlags, generic, ref genericIndex, ref byreflikeIndex);
            }

            public Type GetParamType(int param, Type? generic, ref int genericIndex, ref int byreflikeIndex)
            {
                return GetParamTypeCore(GetParamFlags(param), generic, ref genericIndex, ref byreflikeIndex);
            }

            private Type GetParamTypeCore(ParamFlags flags, Type? generic, ref int genericIndex, ref int byreflikeIndex)
            {
                Type ty;
                if (flags.Has(ParamFlags.ByRefLike))
                {
                    ty = byRefLikes[byreflikeIndex++];
                }
                else
                {
                    Helpers.Assert(generic is not null);
                    ty = generic;
                    genericIndex++;
                }

                if (flags.Has(ParamFlags.ByRef))
                {
                    ty = ty.MakeByRefType();
                }

                return ty;
            }
            

            public TypeReference GetReturnType(ModuleDefinition def, TypeReference? generic, ref int genericIndex, ref int byreflikeIndex)
            {
                var returnFlags = this.returnFlags;
                if (returnFlags == ReturnIsVoid)
                {
                    return def.TypeSystem.Void;
                }

                return GetParamTypeCore(def, returnFlags, generic, ref genericIndex, ref byreflikeIndex);
            }

            public TypeReference GetParamType(ModuleDefinition def, int param, TypeReference? generic, ref int genericIndex, ref int byreflikeIndex)
            {
                return GetParamTypeCore(def, GetParamFlags(param), generic, ref genericIndex, ref byreflikeIndex);
            }

            private TypeReference GetParamTypeCore(ModuleDefinition def, ParamFlags flags, TypeReference? generic, ref int genericIndex, ref int byreflikeIndex)
            {
                TypeReference ty;
                if (flags.Has(ParamFlags.ByRefLike))
                {
                    ty = def.ImportReference(byRefLikes[byreflikeIndex++]);
                }
                else
                {
                    Helpers.Assert(generic is not null);
                    ty = generic;
                    genericIndex++;
                }

                if (flags.Has(ParamFlags.ByRef))
                {
                    ty = new ByReferenceType(ty);
                }

                return ty;
            }

            public Type[] GetGenericArgsFrom(MethodSignature sig, bool returnLast)
            {
                Helpers.Assert(paramCount == sig.ParameterCount);

                if (generics == 0) return [];

                var result = new Type[generics];
                var i = 0;

                // consider return type first
                if (returnFlags != ReturnIsVoid && !returnFlags.Has(ParamFlags.ByRefLike))
                {
                    var ret = sig.ReturnType;
                    if (returnFlags.Has(ParamFlags.ByRef))
                    {
                        Helpers.Assert(ret.IsByRef);
                        ret = ret.GetElementType();
                        Helpers.Assert(ret is not null);
                    }
                    if (returnLast)
                    {
                        result[result.Length - 1] = ret;
                    }
                    else
                    {
                        result[i++] = ret;
                    }
                }

                // then each argument in turn
                var j = 0;
                foreach (var param in sig.Parameters)
                {
                    var flags = GetParamFlags(j++);
                    if (flags.Has(ParamFlags.ByRefLike))
                    {
                        // byreflike, skip
                        continue;
                    }

                    var ty = param;
                    if (flags.Has(ParamFlags.ByRef))
                    {
                        Helpers.Assert(ty.IsByRef);
                        ty = ty.GetElementType();
                        Helpers.Assert(ty is not null);
                    }
                    result[i++] = ty;
                }

                return result;
            }
        }

        private readonly record struct DelegateTypeResult(
            Type DelegateType,
            MethodInfo? InvokeInverse);

        private static readonly ConcurrentDictionary<DelegateTypeCacheKey, (DelegateTypeResult Result, bool ReturnLast)> delegateTypesCache = new();

        public static (Type DelegateType, MethodInfo Invoker) GetDelegateType(MethodSignature sig)
        {
            var cacheKey = DelegateTypeCacheKey.Create(sig);
            var ((typeDef, invokeInv), returnIsLastArg) = delegateTypesCache.GetOrAdd(cacheKey, CreateDelegateType);

            var tyArgs = cacheKey.GetGenericArgsFrom(sig, returnIsLastArg);
            if (typeDef.IsGenericTypeDefinition)
            {
                // need to instantiate
                typeDef = typeDef.MakeGenericType(tyArgs);
            }
            if (invokeInv is not null)
            {
                if (invokeInv.IsGenericMethodDefinition)
                {
                    invokeInv = invokeInv.MakeGenericMethod(tyArgs);
                }
            }
            else
            {
                invokeInv = typeDef.GetMethod("Invoke");
                Helpers.Assert(invokeInv is not null);
            }

            return (typeDef, invokeInv);
        }

        private static (DelegateTypeResult DelegateType, bool ReturnLast) CreateDelegateType(DelegateTypeCacheKey key)
        {
            if (!key.HasByRefLikes)
            {
                // we can use our fast invokers if only the first parameter is a byref
                if (!key.HasByRefs || !key.HasNonFirstByRefs)
                {
                    if (key.NumParams == 0)
                    {
                        // no parameters, can just use Action/Func + their invoke methods
                        var result = key.ReturnsVoid ? typeof(Action) : typeof(Func<>);
                        return (new(result, null), true);
                    }

                    if (key.NumParams <= FastDelegateInvokers.MaxFastInvokerParams)
                    {
                        // we can use our pregenerated FastDelegateInvokers
                        var index = FastDelegateInvokers.EncodeIndex(!key.ReturnsVoid, key.HasByRefs, key.NumParams);
                        if (index < FastDelegateInvokers.Invokers.Length)
                        {
                            var (invoker, del) = FastDelegateInvokers.Invokers[index];
                            return (new(del, invoker), false);
                        }
                    }
                }
            }

            // otherwise, we need to synthesize it
            DelegateTypeResult resultType;

#pragma warning disable CA1031 // Do not catch general exception types
            // If we support MethodBuilder, if it throws, we want to fall back to creating with Cecil.
            // If Cecil succeeds, we're OK swallowing the exception. If it fails, it'll throw, and we'll
            // have gotten an exception in the log anyway.
#if NET9_0_OR_GREATER
            try
            {
                resultType = CreateDelegateTypeWithDynAssembly(key);
            }
            catch (Exception e)
            {
                MMDbgLog.Error($"Creation of delegate type {key} with dynamic assembly failed: {e}");
#else
            {
#endif
                resultType = CreateDelegateTypeWithCecil(key);
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return (resultType, false);
        }

#if NET9_0_OR_GREATER // only do this for net9+ so we don't have to deal with mono with trimmed SRE
        private static readonly Lock dynamicAssemblyLock = new();
        private static AssemblyBuilder? dynamicAssembly;
        private static ModuleBuilder? dynamicModule;

        private static DelegateTypeResult CreateDelegateTypeWithDynAssembly(DelegateTypeCacheKey key)
        {
            lock (dynamicAssemblyLock)
            {
                dynamicAssembly ??= AssemblyBuilder.DefineDynamicAssembly(
                    new("MonoMod.DMDGenerators.DelegateTypeGenerator.DelegateTypes"),
                    AssemblyBuilderAccess.RunAndCollect);

                dynamicModule ??= dynamicAssembly.DefineDynamicModule(dynamicAssembly.FullName ?? "???");

                var typeName = key.ToString();

                var type = dynamicModule.DefineType(typeName,
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class,
                    typeof(MulticastDelegate));

                var ctor = type.DefineConstructor(
                    System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.RTSpecialName | System.Reflection.MethodAttributes.SpecialName,
                    System.Reflection.CallingConventions.HasThis,
                    [typeof(object), typeof(IntPtr)]);
                ctor.SetImplementationFlags(System.Reflection.MethodImplAttributes.Managed | System.Reflection.MethodImplAttributes.Runtime);

                var genericParamNames = Enumerable.Range(0, key.NumGenericParams).Select(i => i.ToString(null, null)).ToArray();
                var genericParams = type.DefineGenericParameters(genericParamNames);
                var genericParamIndex = 0;
                var byreflikeIndex = 0;

                var invoke = type.DefineMethod("Invoke",
                    System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual,
                    System.Reflection.CallingConventions.HasThis);
                invoke.SetImplementationFlags(System.Reflection.MethodImplAttributes.Managed | System.Reflection.MethodImplAttributes.Runtime);

                // return type first
                invoke.SetReturnType(key.GetReturnType(
                    genericParams.Length > genericParamIndex ? genericParams[genericParamIndex] : null,
                    ref genericParamIndex,
                    ref byreflikeIndex));

                // then args
                var parameters = new Type[key.NumParams];
                for (var i = 0; i < key.NumParams; i++)
                {
                    parameters[i] = key.GetParamType(i,
                        genericParams.Length > genericParamIndex ? genericParams[genericParamIndex] : null,
                        ref genericParamIndex,
                        ref byreflikeIndex);
                }

                invoke.SetParameters(parameters);

                var generatedDelegateType = type.CreateType();

                // we also need to generate an invoker
                var invokerType = dynamicModule.DefineType(typeName + "_Invoker",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Class);

                var invokerMethod = invokerType.DefineMethod("Invoker", System.Reflection.MethodAttributes.Static | System.Reflection.MethodAttributes.Public);
                invokerMethod.SetImplementationFlags(System.Reflection.MethodImplAttributes.IL);

                genericParams = invokerMethod.DefineGenericParameters(genericParamNames);
                genericParamIndex = 0;
                byreflikeIndex = 0;

                // first return type
                invokerMethod.SetReturnType(key.GetReturnType(
                    genericParams.Length > genericParamIndex ? genericParams[genericParamIndex] : null,
                    ref genericParamIndex,
                    ref byreflikeIndex));

                // then args
                parameters = new Type[key.NumParams + 1];
                for (var i = 0; i < key.NumParams; i++)
                {
                    parameters[i] = key.GetParamType(i,
                        genericParams.Length > genericParamIndex ? genericParams[genericParamIndex] : null,
                        ref genericParamIndex,
                        ref byreflikeIndex);
                }

                // then a final arg of the delegate type
                parameters[key.NumParams] = generatedDelegateType.MakeGenericType(genericParams);
                invokerMethod.SetParameters(parameters);

                var il = invokerMethod.GetILGenerator();
                il.Emit(OpCodes.Ldarg, key.NumParams);
                for (var i = 0; i < key.NumParams; i++)
                {
                    il.Emit(OpCodes.Ldarg, i);
                }
                il.EmitCall(OpCodes.Callvirt, parameters[key.NumParams].GetMethod("Invoke")!, null);
                il.Emit(OpCodes.Ret);

                var generatedInvokerType = invokerType.CreateType();

                return new(generatedDelegateType, generatedInvokerType.GetMethod(invokerMethod.Name)!);
            }
        }
#endif

        private static DelegateTypeResult CreateDelegateTypeWithCecil(DelegateTypeCacheKey key)
        {
            var typeName = key.ToString();

            using var mod = ModuleDefinition.CreateModule($"MonoMod.DMDGenerators.DelegateTypeGenerator.DelegateTypes.{typeName}", ModuleKind.Dll);

            var ty = new TypeDefinition("", typeName, CecilTypeAttributes.Public | CecilTypeAttributes.Sealed | CecilTypeAttributes.Class);
            mod.Types.Add(ty);

            var ctor = new MethodDefinition(".ctor", CecilMethodAttributes.SpecialName | CecilMethodAttributes.RTSpecialName | CecilMethodAttributes.Public, mod.TypeSystem.Void)
            {
                HasThis = true,
                ImplAttributes = CecilMethodImplAttributes.Managed | CecilMethodImplAttributes.Runtime,
                Parameters =
                {
                    new ParameterDefinition(mod.TypeSystem.Object),
                    new ParameterDefinition(mod.TypeSystem.IntPtr),
                }
            };
            ty.Methods.Add(ctor);

            var genericParams = Enumerable.Range(0, key.NumGenericParams).Select(i => new GenericParameter(i.ToString(null, null), ty)).ToArray();
            ty.GenericParameters.AddRange(genericParams);

            var genericIndex = 0;
            var byreflikeIndex = 0;

            // return type first
            var returnType = key.GetReturnType(mod,
                genericParams.Length > genericIndex ? genericParams[genericIndex] : null,
                ref genericIndex,
                ref byreflikeIndex);
            var invoke = new MethodDefinition("Invoke", CecilMethodAttributes.Public | CecilMethodAttributes.Virtual, returnType)
            {
                HasThis = true,
                ImplAttributes = CecilMethodImplAttributes.Managed | CecilMethodImplAttributes.Runtime,
            };
            ty.Methods.Add(invoke);

            // then args
            for (var i = 0; i < key.NumParams; i++)
            {
                var paramType = key.GetParamType(mod, i,
                    genericParams.Length > genericIndex ? genericParams[genericIndex] : null,
                    ref genericIndex,
                    ref byreflikeIndex);
                invoke.Parameters.Add(new(paramType));
            }

            // also generate the invoker type
            var invokerTy = new TypeDefinition("", typeName + "_Invoker", CecilTypeAttributes.Public | CecilTypeAttributes.Sealed | CecilTypeAttributes.Abstract | CecilTypeAttributes.Class);
            mod.Types.Add(invokerTy);

            var invokerMethod = new MethodDefinition("Invoker", CecilMethodAttributes.Public | CecilMethodAttributes.Virtual, mod.TypeSystem.Void);
            invokerTy.Methods.Add(invokerMethod);

            genericParams = Enumerable.Range(0, key.NumGenericParams).Select(i => new GenericParameter(i.ToString(null, null), invokerMethod)).ToArray();
            invokerMethod.GenericParameters.AddRange(genericParams);

            genericIndex = 0;
            byreflikeIndex = 0;

            // return type first
            returnType = key.GetReturnType(mod,
                genericParams.Length > genericIndex ? genericParams[genericIndex] : null,
                ref genericIndex,
                ref byreflikeIndex);
            invokerMethod.ReturnType = returnType;

            // then args
            for (var i = 0; i < key.NumParams; i++)
            {
                var paramType = key.GetParamType(mod, i,
                    genericParams.Length > genericIndex ? genericParams[genericIndex] : null,
                    ref genericIndex,
                    ref byreflikeIndex);
                invokerMethod.Parameters.Add(new(paramType));
            }

            // then finally the delegate type
            var delegateParamTy = new GenericInstanceType(ty);
            delegateParamTy.GenericArguments.AddRange(genericParams);
            invokerMethod.Parameters.Add(new(delegateParamTy));

            var il = invokerMethod.Body.GetILProcessor();
            il.Emit(CecilOpCodes.Ldarg, key.NumParams);
            for (var i = 0; i < key.NumParams; i++)
            {
                il.Emit(CecilOpCodes.Ldarg, i);
            }

            var invokeRef = new MethodReference(invoke.Name, invoke.ReturnType)
            {
                DeclaringType = delegateParamTy,
                HasThis = invoke.HasThis,
                CallingConvention = invoke.CallingConvention,
            };
            foreach (var p in invoke.Parameters)
            {
                invokeRef.Parameters.Add(new(p.ParameterType));
            }

            il.Emit(CecilOpCodes.Callvirt, invokeRef);
            il.Emit(CecilOpCodes.Ret);

            // then load the assembly
            var asm = ReflectionHelper.Load(mod);

            var resultType = asm.GetType(ty.FullName);
            Helpers.Assert(resultType is not null);

            var invokerType = asm.GetType(invokerTy.FullName);
            Helpers.Assert(invokerType is not null);

            var invoker = invokerType.GetMethod(invokerMethod.Name);
            Helpers.Assert(invoker is not null);

            return new(resultType, invoker);
        }
    }
}
