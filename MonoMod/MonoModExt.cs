using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoMod {

    public delegate IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context);

    public static class MonoModExt {

        public static readonly Regex TypeGenericParamRegex = new Regex(@"\!\d");
        public static readonly Regex MethodGenericParamRegex = new Regex(@"\!\!\d");

        public static MethodBody Clone(this MethodBody o, MethodDefinition m) {
            if (o == null)
                return null;

            MethodBody c = new MethodBody(m);
            c.MaxStackSize = o.MaxStackSize;
            c.InitLocals = o.InitLocals;
            c.LocalVarToken = o.LocalVarToken;

            foreach (Instruction i in o.Instructions)
                c.Instructions.Add(i);
            foreach (ExceptionHandler i in o.ExceptionHandlers)
                c.ExceptionHandlers.Add(i);
            foreach (VariableDefinition i in o.Variables)
                c.Variables.Add(i);

            return c;
        }

        public static bool IsHidden(this SequencePoint sp) {
            return
                sp.StartLine == 0xFEEFEE &&
                sp.EndLine == 0xFEEFEE &&
                sp.StartColumn == 0 &&
                sp.EndColumn == 0;
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

        public static CustomAttribute GetCustomAttribute(this ICustomAttributeProvider cap, string attribute) {
            if (cap == null || !cap.HasCustomAttributes) return null;
            foreach (CustomAttribute attrib in cap.CustomAttributes)
                if (attrib.AttributeType.FullName == attribute)
                    return attrib;
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

            return "orig_" + method.Name;
        }

        public static bool MatchingPlatform(this ICustomAttributeProvider cap) {
            if (cap == null) return true;
            if (!cap.HasCustomAttributes) return true;
            Platform plat = PlatformHelper.Current;
            foreach (CustomAttribute attrib in cap.CustomAttributes) {
                if (attrib.AttributeType.FullName == "MonoMod.MonoModOnPlatform") {
                    CustomAttributeArgument[] plats = (CustomAttributeArgument[]) attrib.ConstructorArguments[0].Value;
                    for (int i = 0; i < plats.Length; i++) {
                        if (PlatformHelper.Current.HasFlag((Platform) plats[i].Value)) {
                            return true;
                        }
                    }
                    return plats.Length == 0;
                }
            }
            return true;
        }

        public static string GetFindableID(this MethodReference method, string name = null, string type = null, bool withType = true) {
            while (method.IsGenericInstance)
                method = ((GenericInstanceMethod) method).ElementMethod;

            StringBuilder builder = new StringBuilder();
            builder
                .Append(method.ReturnType.FullName)
                .Append(" ");

            if (withType)
                builder.Append(type ?? method.DeclaringType.FullName).Append("::");

            builder
                .Append(name ?? method.Name);


            builder.Append("(");

            if (method.HasParameters) {
                Collection<ParameterDefinition> parameters = method.Parameters;
                for (int i = 0; i < parameters.Count; i++) {
                    ParameterDefinition parameter = parameters[i];
                    if (i > 0)
                        builder.Append(",");

                    if (parameter.ParameterType.IsSentinel)
                        builder.Append("...,");

                    builder.Append(parameter.ParameterType.FullName);
                }
            }

            builder.Append(")");

            return builder.ToString();
        }

        public static void UpdateOffsets(this MethodBody body, int instri, int delta) {
            for (int offsi = body.Instructions.Count - 1; instri <= offsi; offsi--)
                body.Instructions[offsi].Offset--;
        }

        public static int GetInt(this Instruction instr) {
            OpCode op = instr.OpCode;
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

        public static void AddRange<T>(this Collection<T> list, Collection<T> other) {
            for (int i = 0; i < other.Count; i++)
                list.Add(other[i]);
        }

        public static GenericParameter GetGenericParameter(this IGenericParameterProvider provider, string name) {
            foreach (GenericParameter param in provider.GenericParameters)
                if (param.Name == name)
                    return param;

            int index;
            if (provider is MethodReference && MethodGenericParamRegex.IsMatch(name))
                if ((index = int.Parse(name.Substring(2))) < provider.GenericParameters.Count)
                    return provider.GenericParameters[index];
                else
                    return new GenericParameter(name, provider).Update(index, GenericParameterType.Method);

            if (provider is TypeReference && TypeGenericParamRegex.IsMatch(name))
                if ((index = int.Parse(name.Substring(1))) < provider.GenericParameters.Count)
                    return provider.GenericParameters[index];
                else
                    return new GenericParameter(name, provider).Update(index, GenericParameterType.Type);

            return
                (provider as TypeSpecification)?.ElementType.GetGenericParameter(name) ??
                (provider as MemberReference)?.DeclaringType?.GetGenericParameter(name);
        }

        public static IMetadataTokenProvider Relink(this IMetadataTokenProvider mtp, Relinker relinker, IGenericParameterProvider context) {
            if (mtp is TypeReference) return ((TypeReference) mtp).Relink(relinker, context);
            if (mtp is MethodReference) return ((MethodReference) mtp).Relink(relinker, context);
            if (mtp is FieldReference) return ((FieldReference) mtp).Relink(relinker, context);
            if (mtp is ParameterDefinition) return ((ParameterDefinition) mtp).Relink(relinker, context);
            throw new InvalidOperationException($"MonoMod can't handle metadata token providers of the type {mtp.GetType()}");
        }

        public static TypeReference Relink(this TypeReference type, Relinker relinker, IGenericParameterProvider context) {
            if (type is TypeSpecification) {
                TypeSpecification ts = (TypeSpecification) type;
                TypeReference relinkedElem = ts.ElementType.Relink(relinker, context);

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

                if (type.IsGenericInstance) {
                    GenericInstanceType git = new GenericInstanceType(relinkedElem);
                    foreach (TypeReference genArg in ((GenericInstanceType) type).GenericArguments)
                        git.GenericArguments.Add(genArg?.Relink(relinker, context));
                    return git;
                }

				throw new InvalidOperationException($"MonoMod can't handle TypeSpecification: {type.FullName} ({type.GetType()})");
            }

            if (type.IsGenericParameter)
                return context.GetGenericParameter(((GenericParameter) type).Name);

            return (TypeReference) relinker(type, context);
        }

        public static MethodReference Relink(this MethodReference method, Relinker relinker, IGenericParameterProvider context) {
            if (method.IsGenericInstance) {
                GenericInstanceMethod methodg = ((GenericInstanceMethod) method);
                GenericInstanceMethod gim = new GenericInstanceMethod(methodg.ElementMethod.Relink(relinker, context));
                foreach (TypeReference arg in methodg.GenericArguments)
                    // Generic arguments for the generic instance are often given by the next higher provider
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

                foreach (TypeReference constraint in param.Constraints) {
                    paramN.Constraints.Add(constraint.Relink(relinker, context));
                }

                relink.GenericParameters.Add(paramN);
            }

            relink.ReturnType = method.ReturnType?.Relink(relinker, method);

            foreach (ParameterDefinition param in method.Parameters) {
                param.ParameterType = param.ParameterType.Relink(relinker, method);
                relink.Parameters.Add(param);
            }

            return (MethodReference) relinker(relink, context);
        }

        public static FieldReference Relink(this FieldReference field, Relinker relinker, IGenericParameterProvider context) {
            TypeReference declaringType = field.DeclaringType.Relink(relinker, context);
            return (FieldReference) relinker(new FieldReference(field.Name, field.FieldType.Relink(relinker, declaringType), declaringType), context);
        }

        public static ParameterDefinition Relink(this ParameterDefinition param, Relinker relinker, IGenericParameterProvider context) {
            param = ((MethodReference) param.Method).Relink(relinker, context).Parameters[param.Index];
            param.ParameterType = param.ParameterType.Relink(relinker, context);
            // Don't foreach when modifying the collection
            for (int i = 0; i < param.CustomAttributes.Count; i++)
                param.CustomAttributes[i] = param.CustomAttributes[i].Relink(relinker, context);
            return param;
        }

        public static ParameterDefinition Clone(this ParameterDefinition param) {
            ParameterDefinition newParam = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType) {
                Constant = param.Constant,
                IsIn = param.IsIn,
                IsLcid = param.IsLcid,
                IsOptional = param.IsOptional,
                IsOut = param.IsOut,
                IsReturnValue = param.IsReturnValue
            };
            foreach (CustomAttribute attrib in param.CustomAttributes)
                newParam.CustomAttributes.Add(attrib.Clone());
            return newParam;
        }

        public static CustomAttribute Relink(this CustomAttribute attrib, Relinker relinker, IGenericParameterProvider context) {
            attrib.Constructor = attrib.Constructor.Relink(relinker, context);
            // Don't foreach when modifying the collection
            for (int i = 0; i < attrib.ConstructorArguments.Count; i++) {
                CustomAttributeArgument attribArg = attrib.ConstructorArguments[i];
                attrib.ConstructorArguments[i] = new CustomAttributeArgument(attribArg.Type.Relink(relinker, context), attribArg.Value);
            }
            for (int i = 0; i < attrib.Fields.Count; i++) {
                CustomAttributeNamedArgument attribArg = attrib.Fields[i];
                attrib.Fields[i] = new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker, context), attribArg.Argument.Value)
                );
            }
            for (int i = 0; i < attrib.Properties.Count; i++) {
                CustomAttributeNamedArgument attribArg = attrib.Properties[i];
                attrib.Properties[i] = new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker, context), attribArg.Argument.Value)
                );
            }
            return attrib;
        }

        public static CustomAttribute Clone(this CustomAttribute attrib) {
            CustomAttribute newAttrib = new CustomAttribute(attrib.Constructor, attrib.GetBlob());
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

        public static MethodDefinition FindMethod(this TypeDefinition type, string findableID) {
            // First pass: With type name (f.e. global searches)
            foreach (MethodDefinition method in type.Methods)
                if (method.GetFindableID() == findableID) return method;
            // Second pass: Without type name (f.e. LinkTo)
            foreach (MethodDefinition method in type.Methods)
                if (method.GetFindableID(withType: false) == findableID) return method;
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

        public static bool HasMethod(this TypeDefinition type, MethodDefinition method)
            => type.FindMethod(method.GetFindableID()) != null;
        public static bool HasProperty(this TypeDefinition type, PropertyDefinition prop)
            => type.FindProperty(prop.Name) != null;
        public static bool HasField(this TypeDefinition type, FieldDefinition field)
            => type.FindField(field.Name) != null;

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

    }
}
