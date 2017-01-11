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

        public static void AddRange<T>(this Collection<T> list, Collection<T> other) {
            for (int i = 0; i < other.Count; i++)
                list.Add(other[i]);
        }

        public static IMetadataTokenProvider Relink(this IMetadataTokenProvider mtp, Relinker relinker) {
            if (mtp is TypeReference) return ((TypeReference) mtp).Relink(relinker);
            if (mtp is MethodReference) return ((MethodReference) mtp).Relink(relinker);
            // TODO all other mtp types
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
                    foreach (TypeReference genArg in ((GenericInstanceType) type).GenericArguments) {
                        git.GenericArguments.Add(genArg.Relink(relinker));
                    }
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
            throw new NotImplementedException();
        }

        public static CustomAttribute Relink(this CustomAttribute attrib, Relinker relinker) {
            CustomAttribute newAttrib = new CustomAttribute(attrib.Constructor.Relink(relinker), attrib.GetBlob());
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

    }
}
