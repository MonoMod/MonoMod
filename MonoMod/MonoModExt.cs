using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod {

    public delegate IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp);

    public static class MonoModExt {

        public static MethodBody Clone(this MethodBody o, MethodDefinition m) {
            if (o == null) {
                return null;
            }

            MethodBody c = new MethodBody(m);
            c.MaxStackSize = o.MaxStackSize;
            c.InitLocals = o.InitLocals;
            c.LocalVarToken = o.LocalVarToken;

            foreach (Instruction i in o.Instructions) {
                c.Instructions.Add(i);
            }
            foreach (ExceptionHandler i in o.ExceptionHandlers) {
                c.ExceptionHandlers.Add(i);
            }
            foreach (VariableDefinition i in o.Variables) {
                c.Variables.Add(i);
            }

            return c;
        }

        public static bool IsHidden(this SequencePoint sp) {
            return
                sp.StartLine == 0xFEEFEE &&
                sp.EndLine == 0xFEEFEE &&
                sp.StartColumn == 0 &&
                sp.EndColumn == 0;
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
        public static bool HasMMAttribute(this ICustomAttributeProvider cap, string attribute) {
            return cap.HasCustomAttribute("MonoMod.MonoMod" + attribute);
        }

        /// <summary>
        /// Determines if the attribute provider has got a specific custom attribute.
        /// </summary>
        /// <returns><c>true</c> if the attribute provider contains the given custom attribute, <c>false</c> otherwise.</returns>
        /// <param name="cap">Attribute provider to check.</param>
        /// <param name="attribute">Attribute.</param>
        public static bool HasCustomAttribute(this ICustomAttributeProvider cap, string attribute) {
            if (cap == null || !cap.HasCustomAttributes) return false;
            foreach (CustomAttribute attrib in cap.CustomAttributes)
                if (attrib.AttributeType.FullName == attribute)
                    return true;
            return false;
        }

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


        public static IMetadataTokenProvider Relink(this IMetadataTokenProvider mtp, Relinker relinker) {
            if (mtp is TypeReference) return ((TypeReference) mtp).Relink(relinker);
            if (mtp is MethodReference) return ((MethodReference) mtp).Relink(relinker);
            if (mtp is FieldReference) return ((FieldReference) mtp).Relink(relinker);
            throw new InvalidOperationException($"MonoMod can't handle metadata token providers of the type {mtp.GetType()}");
        }

        public static TypeReference Relink(this TypeReference type, Relinker relinker) {
            if (type is TypeSpecification) {
                TypeSpecification ts = (TypeSpecification) type;
                TypeReference relinkedElem = ts.ElementType.Relink(relinker);

                if (type.IsByReference)
                    return new ByReferenceType(relinkedElem);

                if (type.IsPointer)
                    return new PointerType(relinkedElem);

                if (type.IsArray)
                    return new ArrayType(relinkedElem, ((ArrayType) type).Dimensions.Count);

                if (type.IsGenericInstance) {
                    GenericInstanceType git = new GenericInstanceType(relinkedElem);
                    foreach (TypeReference genArg in ((GenericInstanceType) type).GenericArguments)
                        git.GenericArguments.Add(genArg.Relink(relinker));
                    return git;
                }

                if (type.IsGenericParameter)
                    return ((GenericParameter) type).Name != null ?
                        new GenericParameter(
                            ((GenericParameter) type).Name,
                            (IGenericParameterProvider) ((GenericParameter) type).Owner.Relink(relinker)
                        ) :
                        new GenericParameter(
                            (IGenericParameterProvider) ((GenericParameter) type).Owner.Relink(relinker)
                        );

                throw new InvalidOperationException($"MonoMod can't handle TypeSpecification: {type.FullName} ({type.GetType()})");
            }

            return (TypeReference) relinker(type);
        }

        public static MethodReference Relink(this MethodReference method, Relinker relinker) {
            if (method.IsGenericInstance) {
                GenericInstanceMethod methodg = ((GenericInstanceMethod) method);
                GenericInstanceMethod gim = new GenericInstanceMethod(methodg.ElementMethod.Relink(relinker));
                foreach (TypeReference arg in methodg.GenericArguments)
                    gim.GenericArguments.Add(arg.Relink(relinker));

                return gim;
            }

            MethodReference relink = new MethodReference(method.Name, method.ReturnType, method.DeclaringType.Relink(relinker));
            relink.ReturnType = method.ReturnType?.Relink(relinker);

            relink.CallingConvention = method.CallingConvention;
            relink.ExplicitThis = method.ExplicitThis;
            relink.HasThis = method.HasThis;

            foreach (ParameterDefinition param in method.Parameters) {
                param.ParameterType = param.ParameterType.Relink(relinker);
                relink.Parameters.Add(param);
            }

            foreach (GenericParameter param in method.GenericParameters) {
                GenericParameter paramN = new GenericParameter(param.Name, param.Owner) {
                    Attributes = param.Attributes,
                    // MetadataToken = param.MetadataToken
                };

                foreach (TypeReference constraint in param.Constraints) {
                    paramN.Constraints.Add(constraint.Relink(relinker));
                }

                relink.GenericParameters.Add(paramN);
            }

            return relink;
        }

        public static FieldReference Relink(this FieldReference field, Relinker relinker) {
            return new FieldReference(field.Name, field.FieldType.Relink(relinker), field.DeclaringType.Relink(relinker));
        }

        public static CustomAttribute Relink(this CustomAttribute attrib, Relinker relinker) {
            attrib.Constructor = attrib.Constructor.Relink(relinker);
            // Don't foreach when modifying the collection
            for (int i = 0; i < attrib.ConstructorArguments.Count; i++) {
                CustomAttributeArgument attribArg = attrib.ConstructorArguments[i];
                attrib.ConstructorArguments[i] = new CustomAttributeArgument(attribArg.Type.Relink(relinker), attribArg.Value);
            }
            for (int i = 0; i < attrib.Fields.Count; i++) {
                CustomAttributeNamedArgument attribArg = attrib.Fields[i];
                attrib.Fields[i] = new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker), attribArg.Argument.Value)
                );
            }
            for (int i = 0; i < attrib.Properties.Count; i++) {
                CustomAttributeNamedArgument attribArg = attrib.Properties[i];
                attrib.Properties[i] = new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker), attribArg.Argument.Value)
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

        public static GenericParameter Relink(this GenericParameter param, Relinker relinker) {
            GenericParameter newParam = new GenericParameter(param.Name, param.Owner) {
                Attributes = param.Attributes
            };
            foreach (TypeReference constraint in param.Constraints)
                newParam.Constraints.Add(constraint.Relink(relinker));
            return newParam;
        }

        public static GenericParameter Clone(this GenericParameter param) {
            GenericParameter newParam = new GenericParameter(param.Name, param.Owner) {
                Attributes = param.Attributes
            };
            foreach (TypeReference constraint in param.Constraints)
                newParam.Constraints.Add(constraint);
            return newParam;
        }

        public static ParameterDefinition Relink(this ParameterDefinition param, Relinker relinker) {
            param.ParameterType = param.ParameterType.Relink(relinker);
            return param;
        }

        public static ParameterDefinition Clone(this ParameterDefinition param)
            => new ParameterDefinition(param.Name, param.Attributes, param.ParameterType) {
                Constant = param.Constant,
                IsIn = param.IsIn,
                IsLcid = param.IsLcid,
                IsOptional = param.IsOptional,
                IsOut = param.IsOut,
                IsReturnValue = param.IsReturnValue
            };

        public static bool EqualMember(this MemberReference member, MemberReference other)
            => member.FullName == other.FullName;

        public static bool HasMethod(this TypeDefinition type, MethodDefinition method) {
            foreach (MethodDefinition methodInType in type.Methods)
                if (method.EqualMember(methodInType)) return true;
            return false;
        }
        public static bool HasProperty(this TypeDefinition type, PropertyDefinition prop) {
            foreach (PropertyDefinition propInType in type.Properties)
                if (prop.EqualMember(propInType)) return true;
            return false;
        }
        public static bool HasField(this TypeDefinition type, FieldDefinition field) {
            foreach (FieldDefinition fieldInType in type.Fields)
                if (field.EqualMember(fieldInType)) return true;
            return false;
        }

        public static MethodDefinition FindMethod(this TypeDefinition type, string fullName) {
            foreach (MethodDefinition method in type.Methods)
                if (method.FullName == fullName) return method;
            return null;
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
