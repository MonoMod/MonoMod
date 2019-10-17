using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils {
    public delegate object FastReflectionDelegate(object target, params object[] args);
    public static class FastReflectionHelper {
        private static readonly Type[] _DynamicMethodDelegateArgs = { typeof(object), typeof(object[]) };
        private static readonly Dictionary<MethodInfo, FastReflectionDelegate> _MethodCache = new Dictionary<MethodInfo, FastReflectionDelegate>();

        private static FastReflectionDelegate _CreateFastDelegate(MethodBase method, bool directBoxValueAccess = true) {
            DynamicMethodDefinition dmd = new DynamicMethodDefinition($"FastReflection<{method.GetID(simple: true)}>", typeof(object), _DynamicMethodDelegateArgs);
            ILProcessor il = dmd.GetILProcessor();

            ParameterInfo[] args = method.GetParameters();

            bool generateLocalBoxValuePtr = true;

            if (!method.IsStatic) {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType.IsValueType) {
                    il.Emit(OpCodes.Unbox_Any, method.DeclaringType);
                }
            }

            for (int i = 0; i < args.Length; i++) {
                Type argType = args[i].ParameterType;
                bool argIsByRef = argType.IsByRef;
                if (argIsByRef)
                    argType = argType.GetElementType();
                bool argIsValueType = argType.IsValueType;

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
                                    dmd.Definition.Body.Variables.Add(new VariableDefinition(new PinnedType(new PointerType(dmd.Definition.Module.TypeSystem.Void))));
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
                il.Emit(OpCodes.Newobj, method as ConstructorInfo);
            } else if (method.IsFinal || !method.IsVirtual) {
                il.Emit(OpCodes.Call, method as MethodInfo);
            } else {
                il.Emit(OpCodes.Callvirt, method as MethodInfo);
            }

            Type returnType = method.IsConstructor ? method.DeclaringType : (method as MethodInfo).ReturnType;
            if (returnType != typeof(void)) {
                if (returnType.IsValueType) {
                    il.Emit(OpCodes.Box, returnType);
                }
            } else {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Ret);

            return (FastReflectionDelegate) dmd.Generate().CreateDelegate(typeof(FastReflectionDelegate));
        }

        public static FastReflectionDelegate CreateFastDelegate(this MethodInfo method, bool directBoxValueAccess = true)
            => GetFastDelegate(method, directBoxValueAccess);
        public static FastReflectionDelegate GetFastDelegate(this MethodInfo method, bool directBoxValueAccess = true) {
            if (_MethodCache.TryGetValue(method, out FastReflectionDelegate dmd))
                return dmd;

            dmd = _CreateFastDelegate(method, directBoxValueAccess);
            _MethodCache.Add(method, dmd);
            return dmd;
        }

    }
}
