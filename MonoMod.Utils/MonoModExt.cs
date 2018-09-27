using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoMod.Utils {
    [MonoMod__OldName__("MonoMod.Relinker")]
    public delegate IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context);

    /// <summary>
    /// Huge collection of MonoMod-related Mono.Cecil extensions.
    /// </summary>
    [MonoMod__OldName__("MonoMod.MonoModExt")]
    public static class MonoModExt {

        public static IDictionary<string, object> SharedData = new Dictionary<string, object>() {
            { "Platform",           (PlatformHelper.Current & ~Platform.X64).ToString() },
            { "PlatformPrefix",     (PlatformHelper.Current & ~Platform.X64).ToString().ToLowerInvariant() + "_" },
            { "Arch",               (PlatformHelper.Current & Platform.X64).ToString() },
            { "Architecture",       (PlatformHelper.Current & Platform.X64).ToString() },
            { "ArchPrefix",         (PlatformHelper.Current & Platform.X64).ToString().ToLowerInvariant() + "_" },
            { "ArchitecturePrefix", (PlatformHelper.Current & Platform.X64).ToString().ToLowerInvariant() + "_" }
        };

        static readonly Regex TypeGenericParamRegex = new Regex(@"\!\d");
        static readonly Regex MethodGenericParamRegex = new Regex(@"\!\!\d");

        static Type t_ParamArrayAttribute = typeof(ParamArrayAttribute);

        public static ModuleDefinition ReadModule(string path, ReaderParameters rp) {
            Retry:
            try {
                return ModuleDefinition.ReadModule(path, rp);
            } catch {
                if (rp.ReadSymbols) {
                    rp.ReadSymbols = false;
                    goto Retry;
                }
                throw;
            }
        }

        public static ModuleDefinition ReadModule(Stream input, ReaderParameters rp) {
            Retry:
            try {
                return ModuleDefinition.ReadModule(input, rp);
            } catch {
                if (rp.ReadSymbols) {
                    rp.ReadSymbols = false;
                    goto Retry;
                }
                throw;
            }
        }

        public static MethodBody Clone(this MethodBody o, MethodDefinition m) {
            if (o == null)
                return null;

            MethodBody c = new MethodBody(m);
            c.MaxStackSize = o.MaxStackSize;
            c.InitLocals = o.InitLocals;
            c.LocalVarToken = o.LocalVarToken;

            c.Instructions.AddRange(o.Instructions);
            c.ExceptionHandlers.AddRange(o.ExceptionHandlers);
            c.Variables.AddRange(o.Variables);

#if !LEGACY
            m.CustomDebugInformations.AddRange(o.Method.CustomDebugInformations);
            m.DebugInformation.SequencePoints.AddRange(o.Method.DebugInformation.SequencePoints);
#endif

            return c;
        }

        public readonly static System.Reflection.FieldInfo f_GenericParameter_position = typeof(GenericParameter).GetField("position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        public readonly static System.Reflection.FieldInfo f_GenericParameter_type = typeof(GenericParameter).GetField("type", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        public static GenericParameter Update(this GenericParameter param, GenericParameter other)
            => param.Update(other.Position, other.Type);
        public static GenericParameter Update(this GenericParameter param, int position, GenericParameterType type) {
            f_GenericParameter_position.SetValue(param, position);
            f_GenericParameter_type.SetValue(param, type);
            return param;
        }

        public static void AddAttribute(this ICustomAttributeProvider cap, MethodReference constructor)
            => cap.AddAttribute(new CustomAttribute(constructor));
        public static void AddAttribute(this ICustomAttributeProvider cap, CustomAttribute attr)
            => cap.CustomAttributes.Add(attr);

        /// <summary>
        /// Determines if the attribute provider has got a specific MonoMod attribute.
        /// </summary>
        /// <returns><c>true</c> if the attribute provider contains the given MonoMod attribute, <c>false</c> otherwise.</returns>
        /// <param name="cap">Attribute provider to check.</param>
        /// <param name="attribute">Attribute.</param>
        public static bool HasMMAttribute(this ICustomAttributeProvider cap, string attribute)
            => cap.HasCustomAttribute("MonoMod.MonoMod" + attribute);
        public static CustomAttribute GetMMAttribute(this ICustomAttributeProvider cap, string attribute)
            => cap.GetCustomAttribute("MonoMod.MonoMod" + attribute);
        public static CustomAttribute GetNextMMAttribute(this ICustomAttributeProvider cap, string attribute)
            => cap.GetNextCustomAttribute("MonoMod.MonoMod" + attribute);

        public static CustomAttribute GetCustomAttribute(this ICustomAttributeProvider cap, string attribute) {
            if (cap == null || !cap.HasCustomAttributes) return null;
            foreach (CustomAttribute attrib in cap.CustomAttributes)
                if (attrib.AttributeType.FullName == attribute)
                    return attrib;
            return null;
        }

        public static CustomAttribute GetNextCustomAttribute(this ICustomAttributeProvider cap, string attribute) {
            if (cap == null || !cap.HasCustomAttributes) return null;
            bool next = false;
            for (int i = 0; i < cap.CustomAttributes.Count; i++) {
                CustomAttribute attrib = cap.CustomAttributes[i];
                if (attrib.AttributeType.FullName != attribute)
                    continue;
                if (!next) {
                    cap.CustomAttributes.RemoveAt(i);
                    i--;
                    next = true;
                    continue;
                }
                return attrib;
            }
            return null;
        }

        /// <summary>
        /// Determines if the attribute provider has got a specific custom attribute.
        /// </summary>
        /// <returns><c>true</c> if the attribute provider contains the given custom attribute, <c>false</c> otherwise.</returns>
        /// <param name="cap">Attribute provider to check.</param>
        /// <param name="attribute">Attribute.</param>
        public static bool HasCustomAttribute(this ICustomAttributeProvider cap, string attribute)
            => cap.GetCustomAttribute(attribute) != null;

        public static string GetOriginalName(this MethodDefinition method) {
            foreach (CustomAttribute attrib in method.CustomAttributes)
                if (attrib.AttributeType.FullName == "MonoMod.MonoModOriginalName")
                    return (string) attrib.ConstructorArguments[0].Value;

            if (method.Name == ".ctor" || method.Name == ".cctor") {
                return "orig_ctor_" + ((MemberReference) method.DeclaringType).GetPatchName();
            }

            return "orig_" + method.Name;
        }

        public static bool MatchingConditionals(this ICustomAttributeProvider cap, ModuleDefinition module)
            => cap.MatchingConditionals(module.Assembly.Name);
        public static bool MatchingConditionals(this ICustomAttributeProvider cap, AssemblyNameReference asmName = null) {
            if (cap == null) return true;
            if (!cap.HasCustomAttributes) return true;
            Platform plat = PlatformHelper.Current;
            bool status = true;
            foreach (CustomAttribute attrib in cap.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod.MonoModOnPlatform") {
                    CustomAttributeArgument[] plats = (CustomAttributeArgument[]) attrib.ConstructorArguments[0].Value;
                    for (int i = 0; i < plats.Length; i++) {
                        if (PlatformHelper.Is((Platform) plats[i].Value)) {
                            // status &= true;
                            continue;
                        }
                    }
                    status &= plats.Length == 0;
                    continue;
                }

                if (attrib.AttributeType.FullName == "MonoMod.MonoModIfFlag") {
                    string flag = (string) attrib.ConstructorArguments[0].Value;
                    object valueObj;
                    bool value;
                    if (!SharedData.TryGetValue(flag, out valueObj) || !(valueObj is bool))
                        if (attrib.ConstructorArguments.Count == 2)
                            value = (bool) attrib.ConstructorArguments[1].Value;
                        else
                            value = true;
                    else
                        value = (bool) valueObj;
                    status &= value;
                    continue;
                }

                if (attrib.AttributeType.FullName == "MonoMod.MonoModTargetModule") {
                    string name = ((string) attrib.ConstructorArguments[0].Value).Inject(SharedData);
                    status &= asmName.Name == name || asmName.FullName == name;
                    continue;
                }
            }
            return status;
        }

        public static string GetFindableID(this MethodReference method, string name = null, string type = null, bool withType = true, bool simple = false) {
            while (method.IsGenericInstance)
                method = ((GenericInstanceMethod) method).ElementMethod;

            StringBuilder builder = new StringBuilder();

            if (simple) {
                if (withType)
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
        public static string GetFindableID(this System.Reflection.MethodBase method, string name = null, string type = null, bool withType = true, bool proxyMethod = false, bool simple = false) {
            while (method is System.Reflection.MethodInfo && method.IsGenericMethod && !method.IsGenericMethodDefinition)
                method = ((System.Reflection.MethodInfo) method).GetGenericMethodDefinition();

            StringBuilder builder = new StringBuilder();

            if (simple) {
                if (withType)
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

                if (Attribute.IsDefined(parameter, t_ParamArrayAttribute))
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

            if (mref is GenericParameter genParamRef) {
                if (!(minfo is Type genParamInfo) || !genParamInfo.IsGenericParameter)
                    return false;
                // Don't check owner as it introduces a circular check.
                /*
                if (!(genParamRef.Owner as MemberReference).Is(genParamInfo.DeclaringMethod ?? (System.Reflection.MemberInfo) genParamInfo.DeclaringType))
                    return false;
                */
                return genParamRef.Position == genParamInfo.GenericParameterPosition;
            }

            if (!(mref.DeclaringType?.Is(minfo.DeclaringType) ?? minfo.DeclaringType == null))
                return false;
            if (mref.Name != minfo.Name)
                return false;

            if (mref is TypeReference) {
                if (!(minfo is Type typeInfo))
                    return false;

                if (mref is GenericInstanceType genTypeRef) {
                    // GenericInstanceType full names differ greatly from their Reflection counterpart,
                    // compared to most other TypeSpecifications.
                    if (!typeInfo.IsGenericType)
                        return false;

                    Collection<TypeReference> paramRefs = genTypeRef.GenericArguments;
                    Type[] paramInfos = typeInfo.GetGenericArguments();
                    if (paramRefs.Count != paramInfos.Length)
                        return false;

                    for (int i = 0; i < paramRefs.Count; i++) {
                        if (!paramRefs[i].Is(paramInfos[i]))
                            return false;
                    }

                    return genTypeRef.ElementType.Is(typeInfo.GetGenericTypeDefinition());
                }

                // DeclaringType was already checked before.
                // Avoid converting nested type separators between + (.NET) and / (cecil)
                if (mref.DeclaringType != null)
                    return mref.Name == typeInfo.Name;
                return mref.FullName == typeInfo.FullName;
            }

            if (mref is MethodReference methodRef) {
                if (!(minfo is System.Reflection.MethodBase methodInfo))
                    return false;

                Collection<ParameterDefinition> paramRefs = methodRef.Parameters;
                System.Reflection.ParameterInfo[] paramInfos = methodInfo.GetParameters();
                if (paramRefs.Count != paramInfos.Length)
                    return false;

                for (int i = 0; i < paramRefs.Count; i++) {
                    TypeReference paramTypeRef = paramRefs[i].ParameterType;
                    if (paramTypeRef is GenericParameter genParamParamRef) {
                        if (genParamParamRef.Owner is MethodReference && methodRef is GenericInstanceMethod genMethodRef) {
                            paramTypeRef = genMethodRef.GenericArguments[genParamParamRef.Position];
                        } else if (genParamParamRef.Owner is TypeReference && methodRef.DeclaringType is GenericInstanceType genTypeRef) {
                            paramTypeRef = genTypeRef.GenericArguments[genParamParamRef.Position];
                        } else {
                            // throw new NotSupportedException("\"Deep\" generic argument parameter type resolving currently not supported");
                            // ... or we're comparing generic params against generic params.
                        }
                    }
                    if (!paramTypeRef.Is(paramInfos[i].ParameterType))
                        return false;
                }

                return true;
            }

            return true;
        }

        public static void UpdateOffsets(this MethodBody body, int instri, int delta) {
            for (int offsi = body.Instructions.Count - 1; instri <= offsi; offsi--)
                body.Instructions[offsi].Offset += delta;
        }

        public static int GetInt(this Instruction instr) {
            OpCode op = instr.OpCode;
            if (op == OpCodes.Ldc_I4_M1) return -1;
            if (op == OpCodes.Ldc_I4_0) return 0;
            if (op == OpCodes.Ldc_I4_1) return 1;
            if (op == OpCodes.Ldc_I4_2) return 2;
            if (op == OpCodes.Ldc_I4_3) return 3;
            if (op == OpCodes.Ldc_I4_4) return 4;
            if (op == OpCodes.Ldc_I4_5) return 5;
            if (op == OpCodes.Ldc_I4_6) return 6;
            if (op == OpCodes.Ldc_I4_7) return 7;
            if (op == OpCodes.Ldc_I4_8) return 8;
            if (op == OpCodes.Ldc_I4_S) return (sbyte) instr.Operand;
            return (int) instr.Operand;
        }
        public static int? GetIntOrNull(this Instruction instr) {
            OpCode op = instr.OpCode;
            if (op == OpCodes.Ldc_I4_M1) return -1;
            if (op == OpCodes.Ldc_I4_0) return 0;
            if (op == OpCodes.Ldc_I4_1) return 1;
            if (op == OpCodes.Ldc_I4_2) return 2;
            if (op == OpCodes.Ldc_I4_3) return 3;
            if (op == OpCodes.Ldc_I4_4) return 4;
            if (op == OpCodes.Ldc_I4_5) return 5;
            if (op == OpCodes.Ldc_I4_6) return 6;
            if (op == OpCodes.Ldc_I4_7) return 7;
            if (op == OpCodes.Ldc_I4_8) return 8;
            if (op == OpCodes.Ldc_I4_S) return (sbyte) instr.Operand;
            if (op == OpCodes.Ldc_I4) return (int) instr.Operand;
            return null;
        }

        public static ParameterDefinition GetParam(this Instruction instr, MethodDefinition method) {
            OpCode op = instr.OpCode;
            int offs = method.HasThis ? -1 : 0;
            if (op == OpCodes.Ldarg_0) return method.HasThis ? null : method.Parameters[offs + 0];
            if (op == OpCodes.Ldarg_1) return method.Parameters[offs + 1];
            if (op == OpCodes.Ldarg_2) return method.Parameters[offs + 2];
            if (op == OpCodes.Ldarg_3) return method.Parameters[offs + 3];
            if (op == OpCodes.Ldarg_S) return (ParameterDefinition) instr.Operand;
            if (op == OpCodes.Ldarg) return (ParameterDefinition) instr.Operand;
            return null;
        }

        public static VariableDefinition GetLocal(this Instruction instr, MethodDefinition method) {
            OpCode op = instr.OpCode;
            if (op == OpCodes.Ldloc_0) return method.Body.Variables[0];
            if (op == OpCodes.Ldloc_1) return method.Body.Variables[1];
            if (op == OpCodes.Ldloc_2) return method.Body.Variables[2];
            if (op == OpCodes.Ldloc_3) return method.Body.Variables[3];
            if (op == OpCodes.Ldloca_S) return (VariableDefinition) instr.Operand;
            if (op == OpCodes.Ldloc) return (VariableDefinition) instr.Operand;
            return null;
        }

        public static void AddRange<T>(this Collection<T> list, Collection<T> other) {
            for (int i = 0; i < other.Count; i++)
                list.Add(other[i]);
        }
        public static void AddRange(this IDictionary dict, IDictionary other) {
            foreach (DictionaryEntry entry in other)
                dict.Add(entry.Key, entry.Value);
        }
        public static void AddRange<K, V>(this IDictionary<K, V> dict, IDictionary<K, V> other) {
            foreach (KeyValuePair<K, V> entry in other)
                dict.Add(entry.Key, entry.Value);
        }
        public static void AddRange<K, V>(this Dictionary<K, V> dict, Dictionary<K, V> other) {
            foreach (KeyValuePair<K, V> entry in other)
                dict.Add(entry.Key, entry.Value);
        }

        public static void PushRange<T>(this Stack<T> stack, IEnumerable<T> other) {
            foreach (T entry in other)
                stack.Push(entry);
        }
        public static void PopRange<T>(this Stack<T> stack, int n) {
            for (int i = 0; i < n; i++)
                stack.Pop();
        }

        public static void EnqueueRange<T>(this Queue<T> queue, IEnumerable<T> other) {
            foreach (T entry in other)
                queue.Enqueue(entry);
        }
        public static void DequeueRange<T>(this Queue<T> queue, int n) {
            for (int i = 0; i < n; i++)
                queue.Dequeue();
        }

        public static T[] Clone<T>(this T[] array, int length) {
            T[] clone = new T[length];
            Array.Copy(array, clone, length);
            return clone;
        }

        public static GenericParameter GetGenericParameter(this IGenericParameterProvider provider, GenericParameter orig) {
            // Don't ask me, that's possible for T[,].Get in "Enter the Gungeon"...?!
            if (provider is GenericParameter && ((GenericParameter) provider).Name == orig.Name)
                return (GenericParameter) provider;

            foreach (GenericParameter param in provider.GenericParameters)
                if (param.Name == orig.Name)
                    return param;

            int index = orig.Position;
            if (provider is MethodReference && orig.DeclaringMethod != null) {
                if (index < provider.GenericParameters.Count)
                    return provider.GenericParameters[index];
                else
                    return new GenericParameter(orig.Name, provider).Update(index, GenericParameterType.Method);
            }

            if (provider is TypeReference && orig.DeclaringType != null)
                if (index < provider.GenericParameters.Count)
                    return provider.GenericParameters[index];
                else
                    return new GenericParameter(orig.Name, provider).Update(index, GenericParameterType.Type);

            return
                (provider as TypeSpecification)?.ElementType.GetGenericParameter(orig) ??
                (provider as MemberReference)?.DeclaringType?.GetGenericParameter(orig);
        }

        public static IMetadataTokenProvider Relink(this IMetadataTokenProvider mtp, Relinker relinker, IGenericParameterProvider context) {
            if (mtp is TypeReference) return ((TypeReference) mtp).Relink(relinker, context);
            if (mtp is MethodReference) return ((MethodReference) mtp).Relink(relinker, context);
            if (mtp is FieldReference) return ((FieldReference) mtp).Relink(relinker, context);
            if (mtp is ParameterDefinition) return ((ParameterDefinition) mtp).Relink(relinker, context);
            // TODO: relink EventDefinitions
            // if (mtp is EventDefinition) return ((EventDefinition) mtp).Relink(relinker, context);
            throw new InvalidOperationException($"MonoMod can't handle metadata token providers of the type {mtp.GetType()}");
        }

        public static TypeReference Relink(this TypeReference type, Relinker relinker, IGenericParameterProvider context) {
            if (type == null)
                return null;

            if (type is TypeSpecification) {
                TypeSpecification ts = (TypeSpecification) type;
                TypeReference relinkedElem = ts.ElementType.Relink(relinker, context);

                if (type.IsSentinel)
                    return new SentinelType(relinkedElem);

                if (type.IsByReference)
                    return new ByReferenceType(relinkedElem);

                if (type.IsPointer)
                    return new PointerType(relinkedElem);

                if (type.IsPinned)
                    return new PinnedType(relinkedElem);

                if (type.IsArray)
                    return new ArrayType(relinkedElem, ((ArrayType) type).Dimensions.Count);

                if (type.IsRequiredModifier)
                    return new RequiredModifierType(((RequiredModifierType) type).ModifierType.Relink(relinker, context), relinkedElem);

                if (type.IsOptionalModifier)
                    return new OptionalModifierType(((OptionalModifierType) type).ModifierType.Relink(relinker, context), relinkedElem);

                if (type.IsGenericInstance) {
                    GenericInstanceType git = new GenericInstanceType(relinkedElem);
                    foreach (TypeReference genArg in ((GenericInstanceType) type).GenericArguments)
                        git.GenericArguments.Add(genArg?.Relink(relinker, context));
                    return git;
                }

                if (type.IsFunctionPointer) {
                    FunctionPointerType fp = (FunctionPointerType) type;
                    fp.ReturnType = fp.ReturnType.Relink(relinker, context);
                    for (int i = 0; i < fp.Parameters.Count; i++)
                        fp.Parameters[i].ParameterType = fp.Parameters[i].ParameterType.Relink(relinker, context);
                    return fp;
                }

                throw new NotSupportedException($"MonoMod can't handle TypeSpecification: {type.FullName} ({type.GetType()})");
            }

            if (type.IsGenericParameter) {
                GenericParameter genParam = context.GetGenericParameter((GenericParameter) type);
                if (genParam == null)
                    throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} {type.FullName} (context: {context})", type, context);
                for (int i = 0; i < genParam.Constraints.Count; i++)
                    if (!genParam.Constraints[i].IsGenericInstance) // That is somehow possible and causes a stack overflow.
                        genParam.Constraints[i] = genParam.Constraints[i].Relink(relinker, context);
                return genParam;
            }

            return (TypeReference) relinker(type, context);
        }

        public static MethodReference Relink(this MethodReference method, Relinker relinker, IGenericParameterProvider context) {
            if (method.IsGenericInstance) {
                GenericInstanceMethod methodg = (GenericInstanceMethod) method;
                GenericInstanceMethod gim = new GenericInstanceMethod(methodg.ElementMethod.Relink(relinker, context));
                foreach (TypeReference arg in methodg.GenericArguments)
                    // Generic arguments for the generic instance are often given by the next higher provider.
                    gim.GenericArguments.Add(arg.Relink(relinker, context));

                return (MethodReference) relinker(gim, context);
            }

            MethodReference relink = new MethodReference(method.Name, method.ReturnType, method.DeclaringType.Relink(relinker, context));

            relink.CallingConvention = method.CallingConvention;
            relink.ExplicitThis = method.ExplicitThis;
            relink.HasThis = method.HasThis;

            foreach (GenericParameter param in method.GenericParameters) {
                GenericParameter paramN = new GenericParameter(param.Name, param.Owner) {
                    Attributes = param.Attributes,
                    // MetadataToken = param.MetadataToken
                }.Update(param);

                relink.GenericParameters.Add(paramN);

                foreach (TypeReference constraint in param.Constraints) {
                    paramN.Constraints.Add(constraint.Relink(relinker, relink));
                }
            }

            relink.ReturnType = relink.ReturnType?.Relink(relinker, relink);

            foreach (ParameterDefinition param in method.Parameters) {
                param.ParameterType = param.ParameterType.Relink(relinker, method);
                relink.Parameters.Add(param);
            }

            return (MethodReference) relinker(relink, context);
        }

        public static IMetadataTokenProvider Relink(this FieldReference field, Relinker relinker, IGenericParameterProvider context) {
            TypeReference declaringType = field.DeclaringType.Relink(relinker, context);
            return relinker(new FieldReference(field.Name, field.FieldType.Relink(relinker, declaringType), declaringType), context);
        }

        public static ParameterDefinition Relink(this ParameterDefinition param, Relinker relinker, IGenericParameterProvider context) {
            param = (param.Method as MethodReference)?.Parameters[param.Index] ?? param;
            ParameterDefinition newParam = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType.Relink(relinker, context)) {
                IsIn = param.IsIn,
                IsLcid = param.IsLcid,
                IsOptional = param.IsOptional,
                IsOut = param.IsOut,
                IsReturnValue = param.IsReturnValue,
                MarshalInfo = param.MarshalInfo
            };
            if (param.HasConstant)
                newParam.Constant = param.Constant;
            return newParam;
        }

        public static ParameterDefinition Clone(this ParameterDefinition param) {
            ParameterDefinition newParam = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType) {
                IsIn = param.IsIn,
                IsLcid = param.IsLcid,
                IsOptional = param.IsOptional,
                IsOut = param.IsOut,
                IsReturnValue = param.IsReturnValue,
                MarshalInfo = param.MarshalInfo
            };
            if (param.HasConstant)
                newParam.Constant = param.Constant;
            foreach (CustomAttribute attrib in param.CustomAttributes)
                newParam.CustomAttributes.Add(attrib.Clone());
            return newParam;
        }

        public static CustomAttribute Relink(this CustomAttribute attrib, Relinker relinker, IGenericParameterProvider context) {
            CustomAttribute newAttrib = new CustomAttribute(attrib.Constructor.Relink(relinker, context));
            foreach (CustomAttributeArgument attribArg in attrib.ConstructorArguments)
                newAttrib.ConstructorArguments.Add(new CustomAttributeArgument(attribArg.Type.Relink(relinker, context), attribArg.Value));
            foreach (CustomAttributeNamedArgument attribArg in attrib.Fields)
                newAttrib.Fields.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker, context), attribArg.Argument.Value))
                );
            foreach (CustomAttributeNamedArgument attribArg in attrib.Properties)
                newAttrib.Properties.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker, context), attribArg.Argument.Value))
                );
            return newAttrib;
        }

        public static CustomAttribute Clone(this CustomAttribute attrib) {
            CustomAttribute newAttrib = new CustomAttribute(attrib.Constructor);
            foreach (CustomAttributeArgument attribArg in attrib.ConstructorArguments)
                newAttrib.ConstructorArguments.Add(new CustomAttributeArgument(attribArg.Type, attribArg.Value));
            foreach (CustomAttributeNamedArgument attribArg in attrib.Fields)
                newAttrib.Fields.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type, attribArg.Argument.Value))
                );
            foreach (CustomAttributeNamedArgument attribArg in attrib.Properties)
                newAttrib.Properties.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type, attribArg.Argument.Value))
                );
            return newAttrib;
        }

        public static GenericParameter Relink(this GenericParameter param, Relinker relinker, IGenericParameterProvider context) {
            GenericParameter newParam = new GenericParameter(param.Name, param.Owner) {
                Attributes = param.Attributes
            }.Update(param);
            foreach (TypeReference constraint in param.Constraints)
                newParam.Constraints.Add(constraint.Relink(relinker, context));
            return newParam;
        }

        public static GenericParameter Clone(this GenericParameter param) {
            GenericParameter newParam = new GenericParameter(param.Name, param.Owner) {
                Attributes = param.Attributes
            }.Update(param);
            foreach (TypeReference constraint in param.Constraints)
                newParam.Constraints.Add(constraint);
            return newParam;
        }

        public static MethodDefinition FindMethod(this TypeDefinition type, string findableID, bool simple = true) {
            if (simple && !findableID.Contains(" ")) {
                // Those shouldn't be reached, unless you're defining a relink map dynamically, which may conflict with itself.
                // First simple pass: With type name (just "Namespace.Type::MethodName")
                foreach (MethodDefinition method in type.Methods)
                    if (method.GetFindableID(simple: true) == findableID) return method;
                // Second simple pass: Without type name (basically name only)
                foreach (MethodDefinition method in type.Methods)
                    if (method.GetFindableID(withType: false, simple: true) == findableID) return method;
            }

            // First pass: With type name (f.e. global searches)
            foreach (MethodDefinition method in type.Methods)
                if (method.GetFindableID() == findableID) return method;
            // Second pass: Without type name (f.e. LinkTo)
            foreach (MethodDefinition method in type.Methods)
                if (method.GetFindableID(withType: false) == findableID) return method;

            return null;
        }
        public static System.Reflection.MethodInfo FindMethod(this Type type, string findableID, bool simple = true) {
            System.Reflection.MethodInfo[] methods = type.GetMethods(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            );
            // First pass: With type name (f.e. global searches)
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID() == findableID) return method;
            // Second pass: Without type name (f.e. LinkTo)
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID(withType: false) == findableID) return method;

            if (!simple)
                return null;

            // Those shouldn't be reached, unless you're defining a relink map dynamically, which may conflict with itself.
            // First simple pass: With type name (just "Namespace.Type::MethodName")
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID(simple: true) == findableID) return method;
            // Second simple pass: Without type name (basically name only)
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID(withType: false, simple: true) == findableID) return method;

            return null;
        }
        public static PropertyDefinition FindProperty(this TypeDefinition type, string name) {
            foreach (PropertyDefinition prop in type.Properties)
                if (prop.Name == name) return prop;
            return null;
        }
        public static FieldDefinition FindField(this TypeDefinition type, string name) {
            foreach (FieldDefinition field in type.Fields)
                if (field.Name == name) return field;
            return null;
        }

        public static EventDefinition FindEvent(this TypeDefinition type, string name) {
            foreach (EventDefinition eventDef in type.Events)
                if (eventDef.Name == name) return eventDef;
            return null;
        }

        public static bool HasMethod(this TypeDefinition type, MethodDefinition method)
            => type.FindMethod(method.GetFindableID(withType: false)) != null;
        public static bool HasProperty(this TypeDefinition type, PropertyDefinition prop)
            => type.FindProperty(prop.Name) != null;
        public static bool HasField(this TypeDefinition type, FieldDefinition field)
            => type.FindField(field.Name) != null;
        public static bool HasEvent(this TypeDefinition type, EventDefinition eventDef)
            => type.FindEvent(eventDef.Name) != null;

        public static void SetPublic(this IMetadataTokenProvider mtp, bool p) {
            if (mtp is TypeDefinition) ((TypeDefinition) mtp).SetPublic(p);
            else if (mtp is FieldDefinition) ((FieldDefinition) mtp).SetPublic(p);
            else if (mtp is MethodDefinition) ((MethodDefinition) mtp).SetPublic(p);
            else if (mtp is PropertyDefinition) ((PropertyDefinition) mtp).SetPublic(p);
            else if (mtp is EventDefinition) ((EventDefinition) mtp).SetPublic(p);
            else throw new InvalidOperationException($"MonoMod can't set metadata token providers of the type {mtp.GetType()} public.");
        }
        public static void SetPublic(this FieldDefinition o, bool p) {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.IsPrivate = !p;
            o.IsPublic = p;
            if (p) o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this MethodDefinition o, bool p) {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.IsPrivate = !p;
            o.IsPublic = p;
            if (p) o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this PropertyDefinition o, bool p) {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.GetMethod?.SetPublic(p);
            o.SetMethod?.SetPublic(p);
            foreach (MethodDefinition method in o.OtherMethods)
                method.SetPublic(p);
            if (p) o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this EventDefinition o, bool p) {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.AddMethod?.SetPublic(p);
            o.RemoveMethod?.SetPublic(p);
            o.InvokeMethod?.SetPublic(p);
            foreach (MethodDefinition method in o.OtherMethods)
                method.SetPublic(p);
            if (p) o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this TypeDefinition o, bool p) {
            if (
                !o.IsDefinition ||
                o.Name == "<PrivateImplementationDetails>" ||
                (o.DeclaringType != null && o.DeclaringType.Name == "<PrivateImplementationDetails>")
            )
                return;
            if (o.DeclaringType == null) {
                o.IsNotPublic = !p;
                o.IsPublic = p;
            } else {
                o.IsNestedPrivate = !p;
                o.IsNestedPublic = p;
                if (p) SetPublic(o.DeclaringType, true);
            }
        }

        public static Collection<T> Filtered<T>(this Collection<T> mtps, List<string> with, List<string> without) where T : IMetadataTokenProvider {
            if (with.Count == 0 && without.Count == 0)
                return mtps;
            Collection<T> mtpsFiltered = new Collection<T>();

            for (int i = 0; i < mtps.Count; i++) {
                IMetadataTokenProvider mtp = mtps[i];

                string name = null;
                string nameAlt = null;
                if (mtp is TypeReference) {
                    name = ((TypeReference) mtp).FullName;
                } else if (mtp is MethodReference) {
                    name = ((MethodReference) mtp).GetFindableID(withType: true);
                    nameAlt = ((MethodReference) mtp).GetFindableID(simple: true);
                } else if (mtp is FieldReference) {
                    name = ((FieldReference) mtp).Name;
                }

                if (without.Count != 0 && (without.Contains(name) || without.Contains(nameAlt)))
                    continue;
                if (with.Count == 0 || with.Contains(name) || with.Contains(nameAlt)) {
                    mtpsFiltered.Add((T) mtp);
                }
            }

            return mtpsFiltered;
        }

        public static IMetadataTokenProvider ImportReference(this ModuleDefinition mod, IMetadataTokenProvider mtp) {
            if (mtp is TypeReference) return mod.ImportReference((TypeReference) mtp);
            if (mtp is FieldReference) return mod.ImportReference((FieldReference) mtp);
            if (mtp is MethodReference) return mod.ImportReference((MethodReference) mtp);
            return mtp;
        }

#if LEGACY
        public static TypeReference ImportReference(this ModuleDefinition mod, TypeReference type)
            => mod.Import(type);
        public static TypeReference ImportReference(this ModuleDefinition mod, Type type, IGenericParameterProvider context)
            => mod.Import(type, context);
        public static FieldReference ImportReference(this ModuleDefinition mod, System.Reflection.FieldInfo field)
            => mod.Import(field);
        public static FieldReference ImportReference(this ModuleDefinition mod, System.Reflection.FieldInfo field, IGenericParameterProvider context)
            => mod.Import(field, context);
        public static MethodReference ImportReference(this ModuleDefinition mod, System.Reflection.MethodBase method)
            => mod.Import(method);
        public static MethodReference ImportReference(this ModuleDefinition mod, System.Reflection.MethodBase method, IGenericParameterProvider context)
            => mod.Import(method, context);
        public static TypeReference ImportReference(this ModuleDefinition mod, TypeReference type, IGenericParameterProvider context)
            => mod.Import(type, context);
        public static TypeReference ImportReference(this ModuleDefinition mod, Type type)
            => mod.Import(type);
        public static FieldReference ImportReference(this ModuleDefinition mod, FieldReference field)
            => mod.Import(field);
        public static MethodReference ImportReference(this ModuleDefinition mod, MethodReference method)
            => mod.Import(method);
        public static MethodReference ImportReference(this ModuleDefinition mod, MethodReference method, IGenericParameterProvider context)
            => mod.Import(method, context);
        public static FieldReference ImportReference(this ModuleDefinition mod, FieldReference field, IGenericParameterProvider context)
            => mod.Import(field, context);
#endif

        public static TypeDefinition SafeResolve(this TypeReference r) {
            try {
                return r.Resolve();
            } catch {
                return null;
            }
        }

        public static FieldDefinition SafeResolve(this FieldReference r) {
            try {
                return r.Resolve();
            } catch {
                return null;
            }
        }

        public static MethodDefinition SafeResolve(this MethodReference r) {
            try {
                return r.Resolve();
            } catch {
                return null;
            }
        }

        public static PropertyDefinition SafeResolve(this PropertyReference r) {
            try {
                return r.Resolve();
            } catch {
                return null;
            }
        }

        public static string GetPatchName(this MemberReference mr) {
            return (mr as ICustomAttributeProvider)?.GetPatchName() ?? mr.Name;
        }
        public static string GetPatchFullName(this MemberReference mr) {
            return (mr as ICustomAttributeProvider)?.GetPatchFullName(mr) ?? mr.FullName;
        }

        private static string GetPatchName(this ICustomAttributeProvider cap) {
            string name;

            CustomAttribute patchAttrib = cap.GetMMAttribute("Patch");
            if (patchAttrib != null) {
                name = ((string) patchAttrib.ConstructorArguments[0].Value).Inject(SharedData);
                int dotIndex = name.LastIndexOf('.');
                if (dotIndex != -1 && dotIndex != name.Length - 1) {
                    name = name.Substring(dotIndex + 1);
                }
                return name;
            }

            // Backwards-compatibility: Check for patch_
            name = ((MemberReference) cap).Name;
            return name.StartsWith("patch_") ? name.Substring(6) : name;
        }
        private static string GetPatchFullName(this ICustomAttributeProvider cap, MemberReference mr) {
            if (cap is TypeReference) {
                TypeReference type = (TypeReference) cap;
                CustomAttribute patchAttrib = cap.GetMMAttribute("Patch");
                string name;

                if (patchAttrib != null) {
                    name = ((string) patchAttrib.ConstructorArguments[0].Value).Inject(SharedData);
                } else {
                    // Backwards-compatibility: Check for patch_
                    name = ((MemberReference) cap).Name;
                    name = name.StartsWith("patch_") ? name.Substring(6) : name;
                }

                if (name.StartsWith("global::"))
                    name = name.Substring(8); // Patch name is refering to a global type.
                else if (name.Contains(".") || name.Contains("/")) { } // Patch name is already a full name.
                else if (!string.IsNullOrEmpty(type.Namespace))
                    name = type.Namespace + "." + name;
                else if (type.IsNested)
                    name = type.DeclaringType.GetPatchFullName() + "/" + name;

                if (mr is TypeSpecification) {
                    // Collect TypeSpecifications and append formats back to front.
                    List<TypeSpecification> formats = new List<TypeSpecification>();
                    TypeSpecification ts = (TypeSpecification) mr;
                    do {
                        formats.Add(ts);
                    } while ((ts = (ts.ElementType as TypeSpecification)) != null);

                    StringBuilder builder = new StringBuilder(name.Length + formats.Count * 4);
                    builder.Append(name);
                    for (int formati = formats.Count - 1; formati > -1; --formati) {
                        ts = formats[formati];

                        if (ts.IsByReference)
                            builder.Append("&");
                        else if (ts.IsPointer)
                            builder.Append("*");
                        else if (ts.IsPinned) { } // FullName not overriden.
                        else if (ts.IsSentinel) { } // FullName not overriden.
                        else if (ts.IsArray) {
                            ArrayType array = (ArrayType) ts;
                            if (array.IsVector)
                                builder.Append("[]");
                            else {
                                builder.Append("[");
                                for (int i = 0; i < array.Dimensions.Count; i++) {
                                    if (i > 0)
                                        builder.Append(",");
                                    builder.Append(array.Dimensions[i].ToString());
                                }
                                builder.Append("]");
                            }
                        } else if (ts.IsRequiredModifier)
                            builder.Append("modreq(").Append(((RequiredModifierType) ts).ModifierType).Append(")");
                        else if (ts.IsOptionalModifier)
                            builder.Append("modopt(").Append(((OptionalModifierType) ts).ModifierType).Append(")");
                        else if (ts.IsGenericInstance) {
                            GenericInstanceType gen = (GenericInstanceType) ts;
                            builder.Append("<");
                            for (int i = 0; i < gen.GenericArguments.Count; i++) {
                                if (i > 0)
                                    builder.Append(",");
                                builder.Append(gen.GenericArguments[i].GetPatchFullName());
                            }
                            builder.Append(">");
                        } else if (ts.IsFunctionPointer) {
                            FunctionPointerType fpt = (FunctionPointerType) ts;
                            builder.Append(" ").Append(fpt.ReturnType.GetPatchFullName()).Append(" *(");
                            if (fpt.HasParameters)
                                for (int i = 0; i < fpt.Parameters.Count; i++) {
                                    ParameterDefinition parameter = fpt.Parameters[i];
                                    if (i > 0)
                                        builder.Append(",");

                                    if (parameter.ParameterType.IsSentinel)
                                        builder.Append("...,");

                                    builder.Append(parameter.ParameterType.FullName);
                                }
                            builder.Append(")");
                        } else
                            throw new NotSupportedException($"MonoMod can't handle TypeSpecification: {type.FullName} ({type.GetType()})");
                    }

                    name = builder.ToString();
                }

                return name;
            }

            if (cap is FieldReference) {
                FieldReference field = (FieldReference) cap;
                return $"{field.FieldType.GetPatchFullName()} {field.DeclaringType.GetPatchFullName()}::{cap.GetPatchName()}";
            }

            if (cap is MethodReference)
                throw new InvalidOperationException("GetPatchFullName not supported on MethodReferences - use GetFindableID instead");

            throw new InvalidOperationException($"GetPatchFullName not supported on type {cap.GetType()}");
        }

        public static bool IsBaseMethodCall(this MethodBody body, MethodReference called) {
            MethodDefinition caller = body.Method;
            if (called == null)
                return false;
            TypeReference calledType = called.DeclaringType;
            while (calledType is TypeSpecification)
                calledType = ((TypeSpecification) calledType).ElementType;
            string calledTypeName = calledType.GetPatchFullName();

            bool callingBaseType = false;
            try {
                TypeDefinition baseType = caller.DeclaringType;
                while ((baseType = baseType.BaseType?.SafeResolve()) != null)
                    if (baseType.GetPatchFullName() == calledTypeName) {
                        callingBaseType = true;
                        break;
                    }
            } catch {
                callingBaseType = caller.DeclaringType.GetPatchFullName() == calledTypeName;
            }
            if (!callingBaseType)
                return false;

            // return caller.IsMatchingSignature(called);
            return true;
        }

        public static bool IsCallvirt(this MethodReference method) {
            if (!method.HasThis)
                return false;
            if (method.DeclaringType.IsValueType)
                return false;
            return true;
        }

        public static bool IsStruct(this TypeReference type) {
            if (!type.IsValueType)
                return false;
            if (type.IsPrimitive)
                return false;
            return true;
        }

        public static void RecalculateILOffsets(this MethodDefinition method) {
            if (!method.HasBody)
                return;
            // Calculate updated offsets required for any further manual fixes.
            int offs = 0;
            for (int i = 0; i < method.Body.Instructions.Count; i++) {
                Instruction instr = method.Body.Instructions[i];
                instr.Offset = offs;
                offs += instr.GetSize();
            }

        }

        // Required for field -> call conversions where the original access was an address access.
        public static void AppendGetAddr(this MethodBody body, Instruction instr, TypeReference type, IDictionary<TypeReference, VariableDefinition> localMap = null) {
            VariableDefinition local;
            if (localMap == null || !localMap.TryGetValue(type, out local)) {
                local = new VariableDefinition(type);
                body.Variables.Add(local);
                if (localMap != null)
                    localMap[type] = local;
            }

            ILProcessor il = body.GetILProcessor();
            Instruction tmp = instr;
            // TODO: Fast stloc / ldloca!
            il.InsertAfter(tmp, tmp = il.Create(OpCodes.Stloc, local));
            il.InsertAfter(tmp, tmp = il.Create(OpCodes.Ldloca, local));
        }

        public static void PrependGetValue(this MethodBody body, Instruction instr, TypeReference type) {
            ILProcessor il = body.GetILProcessor();
            il.InsertBefore(instr, il.Create(OpCodes.Ldobj, type));
        }

        public static void ConvertShortLongOps(this MethodDefinition method) {
            if (!method.HasBody)
                return;
            for (int i = 0; i < method.Body.Instructions.Count; i++) {
                Instruction instr = method.Body.Instructions[i];
                // Change short <-> long operations as the method grows / shrinks.
                if (instr.Operand is Instruction) {
                    int offs = ((Instruction) instr.Operand).Offset - instr.Offset;
                    // sbyte.MinValue is -128, but -127 is the first "long" value.
                    if (offs <= -127 || offs >= 127)
                        instr.OpCode = instr.OpCode.ShortToLongOp();
                    else
                        instr.OpCode = instr.OpCode.LongToShortOp();
                }
            }
        }

        private readonly static Type t_Code = typeof(Code);
        private readonly static Type t_OpCodes = typeof(OpCodes);

        private readonly static Dictionary<int, OpCode> _ShortToLongOp = new Dictionary<int, OpCode>();
        public static OpCode ShortToLongOp(this OpCode op) {
            string name = Enum.GetName(t_Code, op.Code);
            if (!name.EndsWith("_S"))
                return op;
            if (_ShortToLongOp.TryGetValue((int) op.Code, out OpCode found))
                return found;
            return _ShortToLongOp[(int) op.Code] = (OpCode?) t_OpCodes.GetField(name.Substring(0, name.Length - 2))?.GetValue(null) ?? op;
        }

        private readonly static Dictionary<int, OpCode> _LongToShortOp = new Dictionary<int, OpCode>();
        public static OpCode LongToShortOp(this OpCode op) {
            string name = Enum.GetName(t_Code, op.Code);
            if (name.EndsWith("_S"))
                return op;
            if (_LongToShortOp.TryGetValue((int) op.Code, out OpCode found))
                return found;
            return _LongToShortOp[(int) op.Code] = (OpCode?) t_OpCodes.GetField(name + "_S")?.GetValue(null) ?? op;
        }

        // IsMatchingSignature and related methods taken and adapted from the Mono.Linker:
        // https://github.com/mono/linker/blob/e4dfcf006b0705aba6b204aab2d603b781c5fc44/linker/Mono.Linker.Steps/TypeMapStep.cs

        public static bool IsMatchingSignature(this MethodDefinition method, MethodReference candidate) {
            if (method.Parameters.Count != candidate.Parameters.Count)
                return false;

            if (method.Name != candidate.Name)
                return false;

            if (!method.ReturnType._InflateGenericType(method).IsMatchingSignature(
                    candidate.ReturnType._InflateGenericType(candidate)
                ))
                return false;

            if (method.GenericParameters.Count != candidate.GenericParameters.Count)
                return false;

            if (method.HasParameters) {
                for (int i = 0; i < method.Parameters.Count; i++)
                    if (!method.Parameters[i].ParameterType._InflateGenericType(method).IsMatchingSignature(
                        candidate.Parameters[i].ParameterType._InflateGenericType(candidate)
                    ))
                        return false;
            }

            if (!candidate.SafeResolve()?.IsVirtual ?? false)
                return false;

            return true;
        }

        public static bool IsMatchingSignature(this IModifierType a, IModifierType b) {
            if (!a.ModifierType.IsMatchingSignature(b.ModifierType))
                return false;

            return a.ElementType.IsMatchingSignature(b.ElementType);
        }

        public static bool IsMatchingSignature(this TypeSpecification a, TypeSpecification b) {
            if (a.GetType() != b.GetType())
                return false;

            GenericInstanceType gita = a as GenericInstanceType;
            if (gita != null)
                return gita.IsMatchingSignature((GenericInstanceType) b);

            IModifierType mta = a as IModifierType;
            if (mta != null)
                return mta.IsMatchingSignature((IModifierType) b);

            return IsMatchingSignature(a.ElementType, b.ElementType);
        }

        public static bool IsMatchingSignature(this GenericInstanceType a, GenericInstanceType b) {
            if (!a.ElementType.IsMatchingSignature(b.ElementType))
                return false;

            if (a.HasGenericArguments != b.HasGenericArguments)
                return false;

            if (!a.HasGenericArguments)
                return true;

            if (a.GenericArguments.Count != b.GenericArguments.Count)
                return false;

            for (int i = 0; i < a.GenericArguments.Count; i++) {
                if (!a.GenericArguments[i].IsMatchingSignature(b.GenericArguments[i]))
                    return false;
            }

            return true;
        }

        public static bool IsMatchingSignature(this GenericParameter a, GenericParameter b) {
            if (a.Position != b.Position)
                return false;

            if (a.Type != b.Type)
                return false;

            return true;
        }

        public static bool IsMatchingSignature(this TypeReference a, TypeReference b) {
            if (a is TypeSpecification || b is TypeSpecification)
                return 
                    (a is TypeSpecification && b is TypeSpecification) &&
                    ((TypeSpecification) a).IsMatchingSignature((TypeSpecification) b);

            if (a is GenericParameter && b is GenericParameter)
                return ((GenericParameter) a).IsMatchingSignature((GenericParameter) b);

            return a.FullName == b.FullName;
        }

        private static TypeReference _InflateGenericType(this TypeReference type, MethodReference method) {
            if (!(method.DeclaringType is GenericInstanceType))
                return type;
            return _InflateGenericType(method.DeclaringType as GenericInstanceType, type);
        }

        private static TypeReference _InflateGenericType(GenericInstanceType genericInstanceProvider, TypeReference typeToInflate) {
            ArrayType arrayType = typeToInflate as ArrayType;
            if (arrayType != null) {
                TypeReference inflatedElementType = _InflateGenericType(genericInstanceProvider, arrayType.ElementType);

                if (inflatedElementType != arrayType.ElementType)
                    return new ArrayType(inflatedElementType, arrayType.Rank);

                return arrayType;
            }

            GenericInstanceType genericInst = typeToInflate as GenericInstanceType;
            if (genericInst != null) {
                GenericInstanceType result = new GenericInstanceType(genericInst.ElementType);

                for (int i = 0; i < genericInst.GenericArguments.Count; ++i)
                    result.GenericArguments.Add(_InflateGenericType(genericInstanceProvider, genericInst.GenericArguments[i]));

                return result;
            }

            GenericParameter genericParameter = typeToInflate as GenericParameter;
            if (genericParameter != null) {
                if (genericParameter.Owner is MethodReference)
                    return genericParameter;

                TypeDefinition elementType = genericInstanceProvider.ElementType.Resolve();
                GenericParameter parameter = elementType.GetGenericParameter(genericParameter);
                return genericInstanceProvider.GenericArguments[parameter.Position];
            }

            FunctionPointerType functionPointerType = typeToInflate as FunctionPointerType;
            if (functionPointerType != null) {
                FunctionPointerType result = new FunctionPointerType();
                result.ReturnType = _InflateGenericType(genericInstanceProvider, functionPointerType.ReturnType);

                for (int i = 0; i < functionPointerType.Parameters.Count; i++)
                    result.Parameters.Add(new ParameterDefinition(_InflateGenericType(genericInstanceProvider, functionPointerType.Parameters[i].ParameterType)));

                return result;
            }

            IModifierType modifierType = typeToInflate as IModifierType;
            if (modifierType != null) {
                TypeReference modifier = _InflateGenericType(genericInstanceProvider, modifierType.ModifierType);
                TypeReference elementType = _InflateGenericType(genericInstanceProvider, modifierType.ElementType);

                if (modifierType is OptionalModifierType)
                    return new OptionalModifierType(modifier, elementType);

                return new RequiredModifierType(modifier, elementType);
            }

            PinnedType pinnedType = typeToInflate as PinnedType;
            if (pinnedType != null) {
                TypeReference elementType = _InflateGenericType(genericInstanceProvider, pinnedType.ElementType);

                if (elementType != pinnedType.ElementType)
                    return new PinnedType(elementType);

                return pinnedType;
            }

            PointerType pointerType = typeToInflate as PointerType;
            if (pointerType != null) {
                TypeReference elementType = _InflateGenericType(genericInstanceProvider, pointerType.ElementType);

                if (elementType != pointerType.ElementType)
                    return new PointerType(elementType);

                return pointerType;
            }

            ByReferenceType byReferenceType = typeToInflate as ByReferenceType;
            if (byReferenceType != null) {
                TypeReference elementType = _InflateGenericType(genericInstanceProvider, byReferenceType.ElementType);

                if (elementType != byReferenceType.ElementType)
                    return new ByReferenceType(elementType);

                return byReferenceType;
            }

            SentinelType sentinelType = typeToInflate as SentinelType;
            if (sentinelType != null) {
                TypeReference elementType = _InflateGenericType(genericInstanceProvider, sentinelType.ElementType);

                if (elementType != sentinelType.ElementType)
                    return new SentinelType(elementType);

                return sentinelType;
            }

            return typeToInflate;
        }

    }
}
