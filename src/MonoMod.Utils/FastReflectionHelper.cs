using Mono.Cecil.Cil;
using MonoMod.Backports;
using MonoMod.Cil;
using MonoMod.Logs;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils
{
    public sealed class WeakBox
    {
        [SuppressMessage("Design", "CA1051:Do not declare visible instance fields",
            Justification = "This emulates the API of StrongBox<T>, but without being generic.")]
        public object? Value;
    }

    public static class FastReflectionHelper
    {
        private static readonly Type[] FastStructInvokerArgs = { typeof(object), typeof(object), typeof(object?[]) };

        public delegate object? FastInvoker(object? target, params object?[]? args);
        // Semantics:
        //   result is an already extant boxed value tyoe of the return type -OR- a StrongBox<T> where T is the nullable value type
        //   -OR- a WeakBox for class types -OR- null if the method returns void
        //   the implementation will write the result to the object as appropriate
        // the reason it accepts a StrongBox<T> for value types is because of Nullable<T>. Boxing a Nullable<T> results in null if it did
        // not have a value, and so one cannot create a boxed Nullable<T> to pass in.
        public delegate void FastStructInvoker(object? target, object? result, params object?[]? args);

        // Takes a FastStructInvoker and implements a FastInvoker for it, assuming that it takes a non-nullable valuetype
        private static object? FastInvokerForStructInvokerVT<T>(FastStructInvoker invoker, object? target, params object?[]? args)
            where T : struct
        {
            object result = default(T);
            invoker(target, result, args);
            return result;
        }
        private static readonly MethodInfo S2FValueType = typeof(FastReflectionHelper).GetMethod(nameof(FastInvokerForStructInvokerVT), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static class TypedCache<T> where T : struct
        {
            [ThreadStatic]
            public static StrongBox<T?>? NullableStrongBox;
        }

        // Takes a FastStructInvoker and implements a FastInvoker for it, using a StrongBox as the result type.
        // Suitable for Nullable<T>.
        private static object? FastInvokerForStructInvokerNullable<T>(FastStructInvoker invoker, object? target, params object?[]? args)
            where T : struct
        {
            var result = TypedCache<T>.NullableStrongBox ??= new(null);
            invoker(target, result, args);
            return result.Value;
        }
        private static readonly MethodInfo S2FNullable = typeof(FastReflectionHelper).GetMethod(nameof(FastInvokerForStructInvokerNullable), BindingFlags.NonPublic | BindingFlags.Static)!;

        [ThreadStatic]
        private static WeakBox? CachedWeakBox;

        // Takes a FastStructInvoker and implements a FastInvoker for it, using a WeakBox as the result type.
        // Suitable for class types.
        private static object? FastInvokerForStructInvokerClass(FastStructInvoker invoker, object? target, params object?[]? args)
        {
            var result = CachedWeakBox ??= new();
            invoker(target, result, args);
            return result.Value;
        }
        private static readonly MethodInfo S2FClass = typeof(FastReflectionHelper).GetMethod(nameof(FastInvokerForStructInvokerClass), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static object? FastInvokerForStructInvokerVoid(FastStructInvoker invoker, object? target, params object?[]? args)
        {
            invoker(target, null, args);
            return null;
        }
        private static readonly MethodInfo S2FVoid = typeof(FastReflectionHelper).GetMethod(nameof(FastInvokerForStructInvokerVoid), BindingFlags.NonPublic | BindingFlags.Static)!;

        private enum ReturnTypeClass
        {
            Void,
            ValueType,
            Nullable,
            ReferenceType
        }

        private static FastInvoker CreateFastInvoker(FastStructInvoker fsi, ReturnTypeClass retTypeClass, Type returnType)
        {
            switch (retTypeClass)
            {
                case ReturnTypeClass.Void:
                    return S2FVoid.CreateDelegate<FastInvoker>(fsi);
                case ReturnTypeClass.ValueType:
                    return S2FValueType.MakeGenericMethod(returnType).CreateDelegate<FastInvoker>(fsi);
                case ReturnTypeClass.Nullable:
                    return S2FNullable.MakeGenericMethod(Nullable.GetUnderlyingType(returnType)!).CreateDelegate<FastInvoker>(fsi);
                case ReturnTypeClass.ReferenceType:
                    return S2FClass.CreateDelegate<FastInvoker>(fsi);
            }
            throw new NotImplementedException($"Invalid ReturnTypeClass {retTypeClass}");
        }

        private sealed class FSITuple
        {
            public readonly FastStructInvoker FSI;
            public readonly ReturnTypeClass RTC;
            public readonly Type ReturnType;

            public FSITuple(FastStructInvoker fsi, ReturnTypeClass rtc, Type rt)
            {
                FSI = fsi;
                RTC = rtc;
                ReturnType = rt;
            }
        }

        private static ConditionalWeakTable<MemberInfo, FSITuple> fastStructInvokers = new();

        private static FSITuple GetFSITuple(MethodBase method)
        {
            return fastStructInvokers.GetValue(method, _
                => new(CreateMethodInvoker(method, out var rtc, out var rt), rtc, rt));
        }


        private static FSITuple GetFSITuple(FieldInfo field)
        {
            return fastStructInvokers.GetValue(field, _
                => new(CreateFieldInvoker(field, out var rtc, out var rt), rtc, rt));
        }

        private static FSITuple GetFSITuple(MemberInfo member)
            => member switch
            {
                MethodBase mb => GetFSITuple(mb),
                FieldInfo fi => GetFSITuple(fi),
                _ => throw new NotSupportedException($"Member type {member.GetType()} is not supported")
            };

        private static ConditionalWeakTable<FSITuple, FastInvoker> fastInvokers = new();

        private static FastInvoker GetFastInvoker(FSITuple tuple)
            => fastInvokers.GetValue(tuple, static t => CreateFastInvoker(t.FSI, t.RTC, t.ReturnType));

        // NOTE: these are deliberately not extension methods because their result is harder to use correctly
        public static FastStructInvoker GetFastStructInvoker(MethodBase method) => GetFSITuple(method).FSI;
        public static FastStructInvoker GetFastStructInvoker(FieldInfo field) => GetFSITuple(field).FSI;
        public static FastStructInvoker GetFastStructInvoker(MemberInfo member) => GetFSITuple(member).FSI;

        public static FastInvoker GetFastInvoker(this MethodBase method) => GetFastInvoker(GetFSITuple(method));
        public static FastInvoker GetFastInvoker(this FieldInfo field) => GetFastInvoker(GetFSITuple(field));
        public static FastInvoker GetFastInvoker(this MemberInfo member) => GetFastInvoker(GetFSITuple(member));

        // TODO: strongly typed field accessors

        #region Emit FastStructInvoker helpers
        [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
        private static void CheckArgs(
            bool isStatic, object? target,
            int retTypeClass, object? result,
            int expectLen, object?[]? args
        )
        {
            if (!isStatic)
            {
                Helpers.ThrowIfArgumentNull(target);
            }

            switch ((ReturnTypeClass)retTypeClass)
            {
                case ReturnTypeClass.Void:
                    // we don't need to ensure anything about the result
                    break;
                case ReturnTypeClass.ValueType:
                case ReturnTypeClass.Nullable:
                case ReturnTypeClass.ReferenceType:
                    // the result should be non-null
                    Helpers.ThrowIfArgumentNull(result);
                    break;
            }

            if (expectLen == 0)
            {
                return;
            }

            Helpers.ThrowIfArgumentNull(args);
            if (args.Length < expectLen)
            {
                ThrowArgumentOutOfRange();
                [MethodImpl(MethodImplOptions.NoInlining)]
                static void ThrowArgumentOutOfRange()
                    => throw new ArgumentOutOfRangeException(nameof(args), "Argument array has too few arguments!");
            }
        }

        private static readonly MethodInfo CheckArgsMethod = typeof(FastReflectionHelper).GetMethod(nameof(CheckArgs), BindingFlags.NonPublic | BindingFlags.Static)!;

        private const int TargetArgId = -1;
        private const int ResultArgId = -2;

        private static Exception BadArgException(
            int arg,
            RuntimeTypeHandle expectType,
            object? target,
            object? result,
            object?[] args
        )
        {
            var expectedType = Type.GetTypeFromHandle(expectType);
            var realType = arg switch
            {
                TargetArgId => target?.GetType(),
                ResultArgId => result?.GetType(),
                _ => args[arg]?.GetType()
            };

            var argName = arg switch
            {
                TargetArgId => nameof(target),
                ResultArgId => nameof(result),
                _ => DebugFormatter.Format($"args[{arg}]")
            };

            if (realType is null)
                return new ArgumentNullException(argName);

            var message = arg switch
            {
                TargetArgId => DebugFormatter.Format($"Target object is the wrong type; expected {expectedType}, got {realType}"),
                ResultArgId => DebugFormatter.Format($"Result object is the wrong type; expected {expectedType}, got {realType}"),
                _ => DebugFormatter.Format($"Argument {arg} is the wrong type; expected {expectedType}, got {realType}")
            };

            return new ArgumentException(message, argName);
        }

        private static readonly MethodInfo BadArgExceptionMethod = typeof(FastReflectionHelper).GetMethod(nameof(BadArgException), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly FieldInfo WeakBoxValueField = typeof(WeakBox).GetField(nameof(WeakBox.Value))!;

        private static ReturnTypeClass ClassifyType(Type returnType)
        {
            if (returnType == typeof(void))
            {
                return ReturnTypeClass.Void;
            }
            else if (returnType.IsValueType)
            {
                if (Nullable.GetUnderlyingType(returnType) is not null)
                {
                    // this takes a nullable return type
                    return ReturnTypeClass.Nullable;
                }
                else
                {
                    return ReturnTypeClass.ValueType;
                }
            }
            else
            {
                return ReturnTypeClass.ReferenceType;
            }
        }

        private static void EmitCheckArgs(ILCursor il, bool isStatic, ReturnTypeClass rtc, int expectParams)
        {
            // see signature of CheckArgs above
            il.Emit(OpCodes.Ldc_I4, isStatic ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // target
            il.Emit(OpCodes.Ldc_I4, (int)rtc);
            il.Emit(OpCodes.Ldarg_1); // result
            il.Emit(OpCodes.Ldc_I4, expectParams);
            il.Emit(OpCodes.Ldarg_2); // args
            il.Emit(OpCodes.Call, CheckArgsMethod);
        }

        private static void EmitCheckType(ILCursor il, int argId, Type expectType, ILLabel badArgLbl)
        {
            var nextLbl = il.DefineLabel();
            var isByref = expectType.IsByRef;

            VariableDefinition? valueVar = null;

            if (isByref)
            {
                expectType = expectType.GetElementType() ?? expectType;

                var cls = ClassifyType(expectType);
                if (!expectType.IsValueType)
                {
                    valueVar = new VariableDefinition(il.Module.TypeSystem.Object);
                    il.Context.Body.Variables.Add(valueVar);
                    il.Emit(OpCodes.Stloc, valueVar);
                    il.Emit(OpCodes.Ldloc, valueVar);
                }

                EmitCheckByref(il, cls, expectType, badArgLbl, argId);

                // if it's a reference type, we want to load the current, and check it
                if (!expectType.IsValueType)
                {
                    if (valueVar is not null)
                    {
                        il.Emit(OpCodes.Ldloc, valueVar);
                    }
                    EmitLoadByref(il, cls, expectType);
                    il.Emit(OpCodes.Ldind_Ref);
                }
                else
                {
                    // otherweise, by checking the byref, we know it's the right type
                    return;
                }
            }

            if (expectType != typeof(object))
            {
                il.Emit(OpCodes.Isinst, expectType);
            }
            il.Emit(OpCodes.Brtrue, nextLbl);
            // check failed, bad argument
            il.Emit(OpCodes.Ldc_I4, argId);
            il.Emit(OpCodes.Ldtoken, expectType);
            il.Emit(OpCodes.Br, badArgLbl);
            il.MarkLabel(nextLbl);
        }

        private static void EmitCheckAllowNull(ILCursor il, int argId, Type expectType, ILLabel badArgLbl)
        {
            var nextLbl = il.DefineLabel();
            var isByref = expectType.IsByRef;

            VariableDefinition? valueVar = null;

            if (isByref)
            {
                expectType = expectType.GetElementType() ?? expectType;

                var cls = ClassifyType(expectType);
                if (!expectType.IsValueType)
                {
                    valueVar = new VariableDefinition(il.Module.TypeSystem.Object);
                    il.Context.Body.Variables.Add(valueVar);
                    il.Emit(OpCodes.Stloc, valueVar);
                    il.Emit(OpCodes.Ldloc, valueVar);
                }

                EmitCheckByref(il, cls, expectType, badArgLbl, argId);

                // if it's a reference type, we want to load the current, and check it
                if (!expectType.IsValueType)
                {
                    if (valueVar is not null)
                    {
                        il.Emit(OpCodes.Ldloc, valueVar);
                    }
                    EmitLoadByref(il, cls, expectType);
                    il.Emit(OpCodes.Ldind_Ref);
                }
                else
                {
                    // otherweise, by checking the byref, we know it's the right type
                    return;
                }
            }

            if (expectType == typeof(object))
            {
                il.Emit(OpCodes.Pop);
                return;
            }

            if (!expectType.IsValueType || Nullable.GetUnderlyingType(expectType) is not null)
            {
                // we explicitly allow null for reference types and Nullable<T>
                var doCheck = il.DefineLabel();
                var val = new VariableDefinition(il.Module.TypeSystem.Object);
                il.Context.Body.Variables.Add(val);
                il.Emit(OpCodes.Stloc, val);
                il.Emit(OpCodes.Ldloc, val);
                il.Emit(OpCodes.Brtrue, doCheck);
                il.Emit(OpCodes.Br, nextLbl);
                il.MarkLabel(doCheck);
                il.Emit(OpCodes.Ldloc, val);
            }

            // referencetype, or valuetype but not byref
            if (!expectType.IsValueType || (!isByref && expectType.IsValueType))
            {
                EmitCheckType(il, argId, expectType, badArgLbl);
            }
            il.MarkLabel(nextLbl);
        }

        private static void EmitBadArgCall(ILCursor il, ILLabel badArgLbl)
        {
            // emit the bad arg call sequence
            il.MarkLabel(badArgLbl);
            il.Emit(OpCodes.Ldarg_0); // target
            il.Emit(OpCodes.Ldarg_1); // result
            il.Emit(OpCodes.Ldarg_2); // args
            il.Emit(OpCodes.Call, BadArgExceptionMethod);
            il.Emit(OpCodes.Throw);
        }

        private static void EmitCheckByref(ILCursor il, ReturnTypeClass rtc, Type returnType, ILLabel badArgLbl, int argId = ResultArgId)
        {
            // then, we want to check our result type
            switch (rtc)
            {
                case ReturnTypeClass.Void:
                    // do nothing, we don't have a return type
                    break;
                case ReturnTypeClass.ValueType:
                    // for value types, we expect a boxed valuetype of the relevant type
                    var expectType = returnType;
                    goto EmitTypeCheck;
                case ReturnTypeClass.Nullable:
                    // for nullable value types, we expect a StrongBox<returnType>
                    expectType = typeof(StrongBox<>).MakeGenericType(returnType);
                    goto EmitTypeCheck;
                case ReturnTypeClass.ReferenceType:
                    // for reference types, we expect a WeakBox
                    expectType = typeof(WeakBox);
                    goto EmitTypeCheck;
                    EmitTypeCheck:
                    EmitCheckType(il, argId, expectType, badArgLbl);
                    break;
            }
        }

        private static void EmitLoadByref(ILCursor il, ReturnTypeClass rtc, Type returnType)
        {

            // then we actually want to load the reference to it
            switch (rtc)
            {
                case ReturnTypeClass.Void:
                    // no return type, do nothing
                    break;
                case ReturnTypeClass.ValueType:
                    // unbox insn
                    il.Emit(OpCodes.Unbox, returnType);
                    break;
                case ReturnTypeClass.Nullable:
                    // get strong box field
                    var strongBoxResult = typeof(StrongBox<>).MakeGenericType(returnType);
                    var strongBoxField = strongBoxResult.GetField(nameof(StrongBox<int>.Value))!;
                    il.Emit(OpCodes.Ldflda, strongBoxField);
                    break;
                case ReturnTypeClass.ReferenceType:
                    // get weak box field
                    il.Emit(OpCodes.Ldflda, WeakBoxValueField);
                    break;
            }
        }

        private static void EmitLoadArgO(ILCursor il, int arg)
        {
            il.Emit(OpCodes.Ldarg_2); // args
            il.Emit(OpCodes.Ldc_I4, arg);
            il.Emit(OpCodes.Ldelem_Ref);
        }

        private static void EmitStoreByref(ILCursor il, ReturnTypeClass rtc, Type returnType)
        {
            // the only thing left on the stack is the result reference and result, so save the result
            if (rtc is not ReturnTypeClass.Void)
            {
                if (returnType.IsValueType)
                {
                    il.Emit(OpCodes.Stobj, returnType);
                }
                else
                {
                    il.Emit(OpCodes.Stind_Ref);
                }
            }
        }
        #endregion

        private static FastStructInvoker CreateMethodInvoker(MethodBase method, out ReturnTypeClass retTypeClass, out Type retType)
        {
            if (!method.IsStatic && (method.DeclaringType?.IsByRefLike() ?? false))
                throw new ArgumentException("Cannot create reflection invoker for instance method on byref-like type", nameof(method));

            var returnType = method is MethodInfo mi ? mi.ReturnType : method.DeclaringType!;
            retType = returnType;
            if (returnType.IsByRef || returnType.IsByRefLike())
                throw new ArgumentException("Cannot create reflection invoker for method with byref or byref-like return type", nameof(method));

            retTypeClass = ClassifyType(returnType);
            var typeClass = retTypeClass;

            var methParams = method.GetParameters();

            using var dmd = new DynamicMethodDefinition(DebugFormatter.Format($"MM:FastStructInvoker<{method}>"), null, FastStructInvokerArgs);
            using var ilc = new ILContext(dmd.Definition);
            ilc.Invoke(ilc =>
            {
                var il = new ILCursor(ilc);
                // arg 0 is target, arg 1 is result, arg 2 is args array

                // first, we do a check args
                EmitCheckArgs(il, method.IsStatic || method is ConstructorInfo, typeClass, methParams.Length);

                var badArgLbl = il.DefineLabel();

                // then we want to do a check of our target and args
                // first, our target, but only if the method is non-static
                if (!method.IsStatic && method is not ConstructorInfo)
                {
                    var expectType = method.DeclaringType;
                    Helpers.Assert(expectType is not null);

                    il.Emit(OpCodes.Ldarg_0);
                    EmitCheckType(il, TargetArgId, expectType, badArgLbl);
                }

                if (typeClass != ReturnTypeClass.Void)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    EmitCheckByref(il, typeClass, returnType, badArgLbl);
                }

                // then, we want to go through and check all our arguments
                for (var arg = 0; arg < methParams.Length; arg++)
                {
                    var ptype = methParams[arg].ParameterType;
                    if (ptype.IsByRefLike()) // we *can't*, however, support byreflikes, so check for that and throw
                        throw new ArgumentException("Cannot create reflection invoker for method with byref-like argument types", nameof(method));

                    EmitLoadArgO(il, arg);
                    EmitCheckAllowNull(il, arg, ptype, badArgLbl);
                }

                if (typeClass != ReturnTypeClass.Void)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    EmitLoadByref(il, typeClass, returnType);
                }

                // now we can load our target ref if needed
                if (!method.IsStatic && method is not ConstructorInfo)
                {
                    var declType = method.DeclaringType;
                    Helpers.Assert(declType is not null);
                    il.Emit(OpCodes.Ldarg_0);
                    if (declType.IsValueType)
                    {
                        // we want a ref to it, not the boxed value
                        // call unbox
                        il.Emit(OpCodes.Unbox, declType);
                    }
                }

                // and finally, we can load all of our arguments
                for (var arg = 0; arg < methParams.Length; arg++)
                {
                    var nextLbl = il.DefineLabel();
                    var ptype = methParams[arg].ParameterType;
                    var realType = ptype.IsByRef ? ptype.GetElementType() ?? ptype : ptype;

                    EmitLoadArgO(il, arg);
                    if (ptype.IsByRef)
                    {
                        EmitLoadByref(il, ClassifyType(realType), realType);
                    }
                    else if (ptype.IsValueType)
                    {
                        il.Emit(OpCodes.Unbox_Any, realType);
                    }
                }

                // before finally calling the target method
                if (method is ConstructorInfo ci)
                {
                    il.Emit(OpCodes.Newobj, ci);
                }
                else
                {
                    if (method.IsVirtual)
                    {
                        il.Emit(OpCodes.Callvirt, method);
                    }
                    else
                    {
                        il.Emit(OpCodes.Call, method);
                    }
                }

                EmitStoreByref(il, typeClass, returnType);

                // and finally return
                il.Emit(OpCodes.Ret);

                EmitBadArgCall(il, badArgLbl);
            });

            return dmd.Generate().CreateDelegate<FastStructInvoker>();
        }

        // a field invoker can take zero arguments to get the value, or one argument to set the value (and return the new value)
        // note that a caller MUST pass in a result object, even when only setting the field
        private static FastStructInvoker CreateFieldInvoker(FieldInfo field, out ReturnTypeClass retTypeClass, out Type retType)
        {
            if (!field.IsStatic && (field.DeclaringType?.IsByRefLike() ?? false))
                throw new ArgumentException("Cannot create reflection invoker for instance field on byref-like type", nameof(field));

            var returnType = field.FieldType;
            retType = returnType;
            // if the containing type is not a byref or byreflike, then the field type cannot be either
            retTypeClass = ClassifyType(returnType);
            var typeClass = retTypeClass;

            using var dmd = new DynamicMethodDefinition(DebugFormatter.Format($"MM:FastStructInvoker<{field}>"), null, FastStructInvokerArgs);
            using var ilc = new ILContext(dmd.Definition);
            ilc.Invoke(ilc =>
            {
                var il = new ILCursor(ilc);
                // arg 0 is target, arg 1 is result, arg 2 is args array

                EmitCheckArgs(il, field.IsStatic, typeClass, 0);

                var badArgLbl = il.DefineLabel();
                if (!field.IsStatic)
                {
                    var expect = field.DeclaringType!;
                    il.Emit(OpCodes.Ldarg_0);
                    EmitCheckType(il, TargetArgId, expect, badArgLbl);
                }

                il.Emit(OpCodes.Ldarg_1);
                EmitCheckByref(il, typeClass, returnType, badArgLbl);

                // at this point, we must decide whether to get or set
                var getLbl = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Brfalse, getLbl); // args is null
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Blt, getLbl); // args.Length < 1

                // set block

                // check arg
                EmitLoadArgO(il, 0);
                EmitCheckAllowNull(il, 0, field.FieldType, badArgLbl);

                // load ret
                il.Emit(OpCodes.Ldarg_1);
                EmitLoadByref(il, typeClass, returnType);

                // load target ref
                if (!field.IsStatic)
                {
                    var declType = field.DeclaringType;
                    Helpers.Assert(declType is not null);
                    il.Emit(OpCodes.Ldarg_0);
                    if (declType.IsValueType)
                    {
                        // we want a ref to it, not the boxed value
                        // call unbox
                        il.Emit(OpCodes.Unbox, declType);
                    }
                }

                // now load our argument
                EmitLoadArgO(il, 0);
                il.Emit(OpCodes.Unbox_Any, field.FieldType);

                // and store to the field
                if (field.IsStatic)
                {
                    il.Emit(OpCodes.Stsfld, field);
                }
                else
                {
                    il.Emit(OpCodes.Stfld, field);
                }

                // reload our argument and copy it to ret
                EmitLoadArgO(il, 0);
                il.Emit(OpCodes.Unbox_Any, field.FieldType);
                EmitStoreByref(il, typeClass, returnType);

                // and finally return
                il.Emit(OpCodes.Ret);

                il.MarkLabel(getLbl);

                // get block

                // load ret
                il.Emit(OpCodes.Ldarg_1);
                EmitLoadByref(il, typeClass, returnType);

                // load target ref
                if (!field.IsStatic)
                {
                    var declType = field.DeclaringType;
                    Helpers.Assert(declType is not null);
                    il.Emit(OpCodes.Ldarg_0);
                    if (declType.IsValueType)
                    {
                        // we want a ref to it, not the boxed value
                        // call unbox
                        il.Emit(OpCodes.Unbox, declType);
                    }
                }

                // and store to the field
                if (field.IsStatic)
                {
                    il.Emit(OpCodes.Ldsfld, field);
                }
                else
                {
                    il.Emit(OpCodes.Ldfld, field);
                }

                // our value is on stack, save it
                EmitStoreByref(il, typeClass, returnType);

                // and finally return
                il.Emit(OpCodes.Ret);

                EmitBadArgCall(il, badArgLbl);
            });

            return dmd.Generate().CreateDelegate<FastStructInvoker>();
        }

        // TODO: property invokers that behave the same as field invokers, when possible
    }
}
