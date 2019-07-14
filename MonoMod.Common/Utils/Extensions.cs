// FIXME: MERGE MonoModExt AND Extensions, KEEP ONLY WHAT'S NEEDED!

using System;
using System.Reflection;
using SRE = System.Reflection.Emit;
using CIL = Mono.Cecil.Cil;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Mono.Cecil;
using System.Text;
using Mono.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MonoMod.Utils {
    public static partial class Extensions {

        /// <summary>
        /// Creates a delegate of the specified type from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <typeparam name="T">The type of the delegate to create.</typeparam>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate<T>(this MethodBase method) where T : class
            => CreateDelegate(method, typeof(T), null);
        /// <summary>
        /// Creates a delegate of the specified type with the specified target from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <typeparam name="T">The type of the delegate to create.</typeparam>
        /// <param name="target">The object targeted by the delegate.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate<T>(this MethodBase method, object target) where T : class
            => CreateDelegate(method, typeof(T), target);
        /// <summary>
        /// Creates a delegate of the specified type from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <param name="delegateType">The type of the delegate to create.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate(this MethodBase method, Type delegateType)
            => CreateDelegate(method, delegateType, null);
        /// <summary>
        /// Creates a delegate of the specified type with the specified target from this method.
        /// </summary>
        /// <param name="method">The method to create the delegate from.</param>
        /// <param name="delegateType">The type of the delegate to create.</param>
        /// <param name="target">The object targeted by the delegate.</param>
        /// <returns>The delegate for this method.</returns>
        public static Delegate CreateDelegate(this MethodBase method, Type delegateType, object target) {
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
                throw new ArgumentException("Type argument must be a delegate type!");
            if (method is System.Reflection.Emit.DynamicMethod dm)
                return dm.CreateDelegate(delegateType, target);

            // TODO: Check delegate Invoke parameters against method parameters.

#if NETSTANDARD
            // Built-in CreateDelegate is available in .NET Standard
            if (method is System.Reflection.MethodInfo mi)
                return mi.CreateDelegate(delegateType, target);
#endif

            RuntimeMethodHandle handle = method.MethodHandle;
            RuntimeHelpers.PrepareMethod(handle);
            IntPtr ptr = handle.GetFunctionPointer();
            return (Delegate) Activator.CreateInstance(delegateType, target, ptr);
        }

        private static readonly Dictionary<Type, int> _GetManagedSizeCache = new Dictionary<Type, int>() {
            { typeof(void), 0 }
        };
        public static int GetManagedSize(this Type t) {
            if (_GetManagedSizeCache.TryGetValue(t, out int size))
                return size;

            // Note: sizeof is more accurate for the "managed size" than Marshal.SizeOf (marshalled size)
            // It also returns a value for types of which the size cannot be determined otherwise.

            // Marshal.SizeOf(typeof(char)) is 1
            // sizeof(char) is 2

            DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"GetSize<{t.FullName}>",
                typeof(int), Type.EmptyTypes
            );

            ILProcessor il = dmd.GetILProcessor();
            il.Emit(OpCodes.Sizeof, dmd.Definition.Module.ImportReference(t));
            il.Emit(OpCodes.Ret);

            lock (_GetManagedSizeCache) {
                return _GetManagedSizeCache[t] = (dmd.Generate().CreateDelegate<Func<int>>() as Func<int>)();
            }
        }

        public static Type GetThisParamType(this MethodBase method) {
            Type type = method.DeclaringType;
            if (type.IsValueType)
                type = type.MakeByRefType();
            return type;
        }

        private static readonly Dictionary<MethodBase, Func<IntPtr>> _GetLdftnPointerCache = new Dictionary<MethodBase, Func<IntPtr>>();
        public static IntPtr GetLdftnPointer(this MethodBase m) {
            if (_GetLdftnPointerCache.TryGetValue(m, out Func<IntPtr> func))
                return func();

            // Note: ldftn doesn't JIT the method on mono, keeping the class constructor untouched.
            // Its results thus don't always match MethodHandle.GetFunctionPointer().

            DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                $"GetLdftnPointer<{m.GetFindableID(simple: true)}>",
                typeof(int), Type.EmptyTypes
            );

            ILProcessor il = dmd.GetILProcessor();
            il.Emit(OpCodes.Ldftn, dmd.Definition.Module.ImportReference(m));
            il.Emit(OpCodes.Ret);

            lock (_GetLdftnPointerCache) {
                return (_GetLdftnPointerCache[m] = dmd.Generate().CreateDelegate<Func<IntPtr>>() as Func<IntPtr>)();
            }
        }

        private static readonly Type t_Code = typeof(Code);
        private static readonly Type t_OpCodes = typeof(OpCodes);

        private static readonly Dictionary<int, OpCode> _ShortToLongOp = new Dictionary<int, OpCode>();
        public static OpCode ShortToLongOp(this OpCode op) {
            string name = Enum.GetName(t_Code, op.Code);
            if (!name.EndsWith("_S"))
                return op;
            lock (_ShortToLongOp) {
                if (_ShortToLongOp.TryGetValue((int) op.Code, out OpCode found))
                    return found;
                return _ShortToLongOp[(int) op.Code] = (OpCode?) t_OpCodes.GetField(name.Substring(0, name.Length - 2))?.GetValue(null) ?? op;
            }
        }

        private static readonly Dictionary<int, OpCode> _LongToShortOp = new Dictionary<int, OpCode>();
        public static OpCode LongToShortOp(this OpCode op) {
            string name = Enum.GetName(t_Code, op.Code);
            if (name.EndsWith("_S"))
                return op;
            lock (_LongToShortOp) {
                if (_LongToShortOp.TryGetValue((int) op.Code, out OpCode found))
                    return found;
                return _LongToShortOp[(int) op.Code] = (OpCode?) t_OpCodes.GetField(name + "_S")?.GetValue(null) ?? op;
            }
        }

        
        public static string GetFindableID(this MethodReference method, string name = null, string type = null, bool withType = true, bool simple = false) {
            while (method.IsGenericInstance)
                method = ((GenericInstanceMethod) method).ElementMethod;

            StringBuilder builder = new StringBuilder();

            if (simple) {
                if (withType && method.DeclaringType != null)
                    builder.Append(type ?? method.DeclaringType.GetPatchFullName()).Append("::");
                builder.Append(name ?? method.Name);
                return builder.ToString();
            }

            builder
                .Append(method.ReturnType.GetPatchFullName())
                .Append(" ");

            if (withType)
                builder.Append(type ?? method.DeclaringType.GetPatchFullName()).Append("::");

            builder
                .Append(name ?? method.Name);

            if (method.GenericParameters.Count != 0) {
                builder.Append("<");
                Collection<GenericParameter> arguments = method.GenericParameters;
                for (int i = 0; i < arguments.Count; i++) {
                    if (i > 0)
                        builder.Append(",");
                    builder.Append(arguments[i].Name);
                }
                builder.Append(">");
            }

            builder.Append("(");

            if (method.HasParameters) {
                Collection<ParameterDefinition> parameters = method.Parameters;
                for (int i = 0; i < parameters.Count; i++) {
                    ParameterDefinition parameter = parameters[i];
                    if (i > 0)
                        builder.Append(",");

                    if (parameter.ParameterType.IsSentinel)
                        builder.Append("...,");

                    builder.Append(parameter.ParameterType.GetPatchFullName());
                }
            }

            builder.Append(")");

            return builder.ToString();
        }

        public static string GetFindableID(this Mono.Cecil.CallSite method) {
            StringBuilder builder = new StringBuilder();

            builder
                .Append(method.ReturnType.GetPatchFullName())
                .Append(" ");

            builder.Append("(");

            if (method.HasParameters) {
                Collection<ParameterDefinition> parameters = method.Parameters;
                for (int i = 0; i < parameters.Count; i++) {
                    ParameterDefinition parameter = parameters[i];
                    if (i > 0)
                        builder.Append(",");

                    if (parameter.ParameterType.IsSentinel)
                        builder.Append("...,");

                    builder.Append(parameter.ParameterType.GetPatchFullName());
                }
            }

            builder.Append(")");

            return builder.ToString();
        }

        private static readonly Type t_ParamArrayAttribute = typeof(ParamArrayAttribute);
        public static string GetFindableID(this System.Reflection.MethodBase method, string name = null, string type = null, bool withType = true, bool proxyMethod = false, bool simple = false) {
            while (method is System.Reflection.MethodInfo && method.IsGenericMethod && !method.IsGenericMethodDefinition)
                method = ((System.Reflection.MethodInfo) method).GetGenericMethodDefinition();

            StringBuilder builder = new StringBuilder();

            if (simple) {
                if (withType && method.DeclaringType != null)
                    builder.Append(type ?? method.DeclaringType.FullName).Append("::");
                builder.Append(name ?? method.Name);
                return builder.ToString();
            }

            builder
                .Append((method as System.Reflection.MethodInfo)?.ReturnType?.FullName ?? "System.Void")
                .Append(" ");

            if (withType)
                builder.Append(type ?? method.DeclaringType.FullName.Replace("+", "/")).Append("::");

            builder
                .Append(name ?? method.Name);

            if (method.ContainsGenericParameters) {
                builder.Append("<");
                Type[] arguments = method.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++) {
                    if (i > 0)
                        builder.Append(",");
                    builder.Append(arguments[i].Name);
                }
                builder.Append(">");
            }

            builder.Append("(");

            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            for (int i = proxyMethod ? 1 : 0; i < parameters.Length; i++) {
                System.Reflection.ParameterInfo parameter = parameters[i];
                if (i > (proxyMethod ? 1 : 0))
                    builder.Append(",");

#if NETSTANDARD
                if (System.Reflection.CustomAttributeExtensions.IsDefined(parameter, t_ParamArrayAttribute, false))
#else
                if (parameter.GetCustomAttributes(t_ParamArrayAttribute, false).Length != 0)
#endif
                    builder.Append("...,");

                builder.Append(parameter.ParameterType.FullName);
            }

            builder.Append(")");

            return builder.ToString();
        }

        public static bool Is(this System.Reflection.MemberInfo minfo, MemberReference mref)
            => mref.Is(minfo);
        public static bool Is(this MemberReference mref, System.Reflection.MemberInfo minfo) {
            if (mref == null)
                return false;

            TypeReference mrefDecl = mref.DeclaringType;
            if (mrefDecl?.FullName == "<Module>")
                mrefDecl = null;

            if (mref is GenericParameter genParamRef) {
                if (!(minfo is Type genParamInfo))
                    return false;
                
                if (!genParamInfo.IsGenericParameter) {
                    if (genParamRef.Owner is IGenericInstance genParamRefOwner)
                        return genParamRefOwner.GenericArguments[genParamRef.Position].Is(genParamInfo);
                    else
                        return false;
                }

                // Don't check owner as it introduces a circular check.
                /*
                if (!(genParamRef.Owner as MemberReference).Is(genParamInfo.DeclaringMethod ?? (System.Reflection.MemberInfo) genParamInfo.DeclaringType))
                    return false;
                */
                return genParamRef.Position == genParamInfo.GenericParameterPosition;
            }

            if (minfo.DeclaringType != null) {
                if (mrefDecl == null)
                    return false;

                Type declType = minfo.DeclaringType;

                if (minfo is Type) {
                    // Note: type.DeclaringType is supposed to == type.DeclaringType.GetGenericTypeDefinition()
                    // For whatever reason, old versions of mono (f.e. shipped with Unity 5.0.3) break this,
                    // requiring us to call .GetGenericTypeDefinition() manually instead.
                    if (declType.IsGenericType && !declType.IsGenericTypeDefinition)
                        declType = declType.GetGenericTypeDefinition();
                }

                if (!mrefDecl.Is(declType))
                    return false;

            } else if (mrefDecl != null)
                return false;

            // Note: This doesn't work for TypeSpecification, as the reflection-side type.Name changes according to any modifiers (f.e. IsArray).
            if (!(mref is TypeSpecification) && mref.Name != minfo.Name)
                return false;

            if (mref is TypeReference typeRef) {
                if (!(minfo is Type typeInfo))
                    return false;

                if (typeInfo.IsGenericParameter)
                    return false;

                if (mref is GenericInstanceType genTypeRef) {
                    if (!typeInfo.IsGenericType)
                        return false;

                    Collection<TypeReference> gparamRefs = genTypeRef.GenericArguments;
                    Type[] gparamInfos = typeInfo.GetGenericArguments();
                    if (gparamRefs.Count != gparamInfos.Length)
                        return false;

                    for (int i = 0; i < gparamRefs.Count; i++) {
                        if (!gparamRefs[i].Is(gparamInfos[i]))
                            return false;
                    }

                    return genTypeRef.ElementType.Is(typeInfo.GetGenericTypeDefinition());

                } else if (typeRef.HasGenericParameters) {
                    if (!typeInfo.IsGenericType)
                        return false;

                    Collection<GenericParameter> gparamRefs = typeRef.GenericParameters;
                    Type[] gparamInfos = typeInfo.GetGenericArguments();
                    if (gparamRefs.Count != gparamInfos.Length)
                        return false;

                    for (int i = 0; i < gparamRefs.Count; i++) {
                        if (!gparamRefs[i].Is(gparamInfos[i]))
                            return false;
                    }

                } else if (typeInfo.IsGenericType)
                    return false;

                if (mref is ArrayType arrayTypeRef) {
                    if (!typeInfo.IsArray)
                        return false;

                    return arrayTypeRef.Dimensions.Count == typeInfo.GetArrayRank() && arrayTypeRef.ElementType.Is(typeInfo.GetElementType());
                }

                if (mref is ByReferenceType byRefTypeRef) {
                    if (!typeInfo.IsByRef)
                        return false;

                    return byRefTypeRef.ElementType.Is(typeInfo.GetElementType());
                }

                if (mref is PointerType ptrTypeRef) {
                    if (!typeInfo.IsPointer)
                        return false;

                    return ptrTypeRef.ElementType.Is(typeInfo.GetElementType());
                }

                if (mref is TypeSpecification typeSpecRef)
                    // Note: There are TypeSpecifications which map to non-ElementType-y reflection Types.
                    return typeSpecRef.ElementType.Is(typeInfo.HasElementType ? typeInfo.GetElementType() : typeInfo);

                // DeclaringType was already checked before.
                // Avoid converting nested type separators between + (.NET) and / (cecil)
                if (mrefDecl != null)
                    return mref.Name == typeInfo.Name;
                return mref.FullName == typeInfo.FullName.Replace("+", "/");
            }

            if (mref is MethodReference methodRef) {
                if (!(minfo is System.Reflection.MethodBase methodInfo))
                    return false;

                Collection<ParameterDefinition> paramRefs = methodRef.Parameters;
                System.Reflection.ParameterInfo[] paramInfos = methodInfo.GetParameters();
                if (paramRefs.Count != paramInfos.Length)
                    return false;

                if (mref is GenericInstanceMethod genMethodRef) {
                    if (!methodInfo.IsGenericMethod)
                        return false;

                    Collection<TypeReference> gparamRefs = genMethodRef.GenericArguments;
                    Type[] gparamInfos = methodInfo.GetGenericArguments();
                    if (gparamRefs.Count != gparamInfos.Length)
                        return false;

                    for (int i = 0; i < gparamRefs.Count; i++) {
                        if (!gparamRefs[i].Is(gparamInfos[i]))
                            return false;
                    }

                    return genMethodRef.ElementMethod.Is((methodInfo as System.Reflection.MethodInfo)?.GetGenericMethodDefinition() ?? methodInfo);

                } else if (methodRef.HasGenericParameters) {
                    if (!methodInfo.IsGenericMethod)
                        return false;

                    Collection<GenericParameter> gparamRefs = methodRef.GenericParameters;
                    Type[] gparamInfos = methodInfo.GetGenericArguments();
                    if (gparamRefs.Count != gparamInfos.Length)
                        return false;

                    for (int i = 0; i < gparamRefs.Count; i++) {
                        if (!gparamRefs[i].Is(gparamInfos[i]))
                            return false;
                    }

                } else if (methodInfo.IsGenericMethod)
                    return false;

                Relinker resolver = null;
                resolver = (paramMemberRef, ctx) => paramMemberRef is TypeReference paramTypeRef ? ResolveParameter(paramTypeRef) : paramMemberRef;
                TypeReference ResolveParameter(TypeReference paramTypeRef) {
                    if (paramTypeRef is GenericParameter paramGenParamTypeRef) {
                        if (paramGenParamTypeRef.Owner is MethodReference && methodRef is GenericInstanceMethod paramGenMethodRef)
                            return paramGenMethodRef.GenericArguments[paramGenParamTypeRef.Position];

                        if (paramGenParamTypeRef.Owner is TypeReference paramGenParamTypeRefOwnerType && methodRef.DeclaringType is GenericInstanceType genTypeRefRef &&
                            paramGenParamTypeRefOwnerType.FullName == genTypeRefRef.ElementType.FullName) // This is to prevent List<Tuple<...>> checks from incorrectly checking Tuple's args in List.
                            return genTypeRefRef.GenericArguments[paramGenParamTypeRef.Position];

                        return paramTypeRef;
                    }

                    if (paramTypeRef == methodRef.DeclaringType.GetElementType())
                        return methodRef.DeclaringType;

                    return paramTypeRef;
                }

                if (!methodRef.ReturnType.Relink(resolver, null).Is(((methodInfo as System.Reflection.MethodInfo)?.ReturnType ?? typeof(void))) &&
                    !methodRef.ReturnType.Is(((methodInfo as System.Reflection.MethodInfo)?.ReturnType ?? typeof(void))))
                    return false;

                for (int i = 0; i < paramRefs.Count; i++)
                    if (!paramRefs[i].ParameterType.Relink(resolver, null).Is(paramInfos[i].ParameterType) &&
                        !paramRefs[i].ParameterType.Is(paramInfos[i].ParameterType))
                        return false;

                return true;
            }

            if (mref is FieldReference && !(minfo is System.Reflection.FieldInfo))
                return false;

            if (mref is PropertyReference && !(minfo is System.Reflection.PropertyInfo))
                return false;

            if (mref is EventReference && !(minfo is System.Reflection.EventInfo))
                return false;

            return true;
        }

    }
}
