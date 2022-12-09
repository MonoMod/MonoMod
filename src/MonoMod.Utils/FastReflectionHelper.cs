using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Logs;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils {
    public delegate object? FastReflectionInvoker(object? target, params object?[]? args);
    public static class FastReflectionHelper {
        private static readonly Type[] _DynamicMethodDelegateArgs = { typeof(object), typeof(object[]) };
        private static readonly Dictionary<MethodInfo, FastReflectionInvoker> _MethodCache = new();

        private static FastReflectionInvoker CreateFastDelegate(MethodBase method, bool directBoxValueAccess = true) {
            using var dmd = new DynamicMethodDefinition(DebugFormatter.Format($"FastReflection<{method}>"), typeof(object), _DynamicMethodDelegateArgs);
            ILProcessor il = dmd.GetILProcessor();

            ParameterInfo[] args = method.GetParameters();

            var generateLocalBoxValuePtr = true;

            if (!method.IsStatic) {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType?.IsValueType ?? false) {
                    il.Emit(OpCodes.Unbox_Any, method.DeclaringType);
                }
            }

            for (var i = 0; i < args.Length; i++) {
                var argType = args[i].ParameterType;
                var argIsByRef = argType.IsByRef;
                if (argIsByRef)
                    argType = argType.GetElementType() ?? argType;
                var argIsValueType = argType.IsValueType;

                if (argIsByRef && argIsValueType && !directBoxValueAccess) {
                    // Used later when storing back the reference to the new box in the array.
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4, i);
                }

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);

                if (argIsByRef && !argIsValueType) {
                    il.Emit(OpCodes.Ldelema, typeof(object));
                } else {
                    il.Emit(OpCodes.Ldelem_Ref);
                    if (argIsValueType) {
                        if (!argIsByRef || !directBoxValueAccess) {
                            // if !directBoxValueAccess, create a new box if required
                            il.Emit(OpCodes.Unbox_Any, argType);
                            if (argIsByRef) {
                                // box back
                                il.Emit(OpCodes.Box, argType);

                                // store new box value address to local 0
                                il.Emit(OpCodes.Dup);
                                il.Emit(OpCodes.Unbox, argType);
                                if (generateLocalBoxValuePtr) {
                                    generateLocalBoxValuePtr = false;
                                    dmd.Definition!.Body.Variables.Add(new VariableDefinition(new PinnedType(new PointerType(dmd.Definition.Module.TypeSystem.Void))));
                                }
                                il.Emit(OpCodes.Stloc_0);

                                // arr and index set up already
                                il.Emit(OpCodes.Stelem_Ref);

                                // load address back to stack
                                il.Emit(OpCodes.Ldloc_0);
                            }
                        } else {
                            // if directBoxValueAccess, emit unbox (get value address)
                            il.Emit(OpCodes.Unbox, argType);
                        }
                    }
                }
            }

            if (method.IsConstructor) {
                il.Emit(OpCodes.Newobj, (ConstructorInfo)method);
            } else if (method.IsFinal || !method.IsVirtual) {
                il.Emit(OpCodes.Call, (MethodInfo)method);
            } else {
                il.Emit(OpCodes.Callvirt, (MethodInfo)method);
            }

            var returnType = method.IsConstructor ? method.DeclaringType : ((MethodInfo)method).ReturnType;
            if (returnType != typeof(void)) {
                if (returnType is not null && returnType.IsValueType) {
                    il.Emit(OpCodes.Box, returnType);
                }
            } else {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Ret);

            return (FastReflectionInvoker) dmd.Generate().CreateDelegate(typeof(FastReflectionInvoker));
        }

        public static FastReflectionInvoker CreateFastDelegate(this MethodInfo method, bool directBoxValueAccess = true)
            => GetFastDelegate(method, directBoxValueAccess);
        public static FastReflectionInvoker GetFastDelegate(this MethodInfo method, bool directBoxValueAccess = true) {
            Helpers.ThrowIfArgumentNull(method);
            if (_MethodCache.TryGetValue(method, out var dmd))
                return dmd;

            dmd = CreateFastDelegate((MethodBase) method, directBoxValueAccess);
            _MethodCache.Add(method, dmd);
            return dmd;
        }

    }
}
