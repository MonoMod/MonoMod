using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.Utils {
    [MonoMod__OldName__("MonoMod.Helpers.DynamicMethodDelegate")]
    public delegate object FastReflectionDelegate(object target, params object[] args);
    /// <summary>
    /// Based on ReflectionHelper from http://theinstructionlimit.com/fast-net-reflection and FEZ. Thanks, Renaud!
    /// </summary>
    [MonoMod__OldName__("MonoMod.Helpers.ReflectionHelper")]
    public static class FastReflectionHelper {
        private static readonly Type[] _DynamicMethodDelegateArgs = { typeof(object), typeof(object[]) };
        private static readonly IDictionary<MethodInfo, FastReflectionDelegate> _MethodCache = new Dictionary<MethodInfo, FastReflectionDelegate>();

        private static readonly MethodInfo m_Console_WriteLine = typeof(Console).GetMethod("WriteLine", new Type[] { typeof(object) });
        private static readonly MethodInfo m_object_GetType = typeof(object).GetMethod("GetType");

        [Obsolete("Use CreateFastDelegate instead.")]
        public static FastReflectionDelegate CreateDelegate(MethodBase method, bool directBoxValueAccess = true)
            => CreateFastDelegate(method, directBoxValueAccess);
        public static FastReflectionDelegate CreateFastDelegate(this MethodBase method, bool directBoxValueAccess = true) {
            DynamicMethod dynam = new DynamicMethod(string.Empty, typeof(object), _DynamicMethodDelegateArgs, typeof(FastReflectionHelper).Module, true);
            ILGenerator il = dynam.GetILGenerator();

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
                    il.EmitFast_Ldc_I4(i);
                }

                il.Emit(OpCodes.Ldarg_1);
                il.EmitFast_Ldc_I4(i);

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
                                    // Yes, you're seeing this right - a local of type void* to store the box value address!
                                    il.DeclareLocal(typeof(void*), true);
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

            return (FastReflectionDelegate) dynam.CreateDelegate(typeof(FastReflectionDelegate));
        }


        public static T CreateJmpDelegate<T>(this MethodBase method) {
            Type t = typeof(T);
            MethodInfo invoke = t.GetMethod("Invoke");

            ParameterInfo[] args = invoke.GetParameters();
            Type[] argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                argTypes[i] = args[i].ParameterType;

            DynamicMethod dynam = new DynamicMethod(string.Empty, invoke.ReturnType, argTypes, typeof(FastReflectionHelper).Module, true);
            ILGenerator il = dynam.GetILGenerator();

            il.Emit(OpCodes.Jmp, (MethodInfo) method);

            return (T) (object) dynam.CreateDelegate(t);
        }

        [Obsolete("Use GetFastDelegate instead.")]
        public static FastReflectionDelegate GetDelegate(MethodInfo method, bool directBoxValueAccess = true)
            => GetFastDelegate(method, directBoxValueAccess);
        public static FastReflectionDelegate GetFastDelegate(this MethodInfo method, bool directBoxValueAccess = true) {
            if (_MethodCache.TryGetValue(method, out FastReflectionDelegate dmd))
                return dmd;

            dmd = CreateFastDelegate(method, directBoxValueAccess);
            _MethodCache.Add(method, dmd);
            return dmd;
        }

        private static void EmitFast_Ldc_I4(this ILGenerator il, int value) {
            switch (value) {
                case -1:
                    il.Emit(OpCodes.Ldc_I4_M1); return;
                case 0:
                    il.Emit(OpCodes.Ldc_I4_0); return;
                case 1:
                    il.Emit(OpCodes.Ldc_I4_1); return;
                case 2:
                    il.Emit(OpCodes.Ldc_I4_2); return;
                case 3:
                    il.Emit(OpCodes.Ldc_I4_3); return;
                case 4:
                    il.Emit(OpCodes.Ldc_I4_4); return;
                case 5:
                    il.Emit(OpCodes.Ldc_I4_5); return;
                case 6:
                    il.Emit(OpCodes.Ldc_I4_6); return;
                case 7:
                    il.Emit(OpCodes.Ldc_I4_7); return;
                case 8:
                    il.Emit(OpCodes.Ldc_I4_8); return;
            }
            if (value > -129 && value < 128)
                il.Emit(OpCodes.Ldc_I4_S, (sbyte) value);
            else
                il.Emit(OpCodes.Ldc_I4, value);
        }

    }
}
