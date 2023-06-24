using AsmResolver.DotNet.Signatures;
using AsmResolver;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

#pragma warning disable RS0030 // Do not use banned APIs
#pragma warning disable CA1062 // Validate arguments of public methods
#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
// This is a slightly modified version of ASmResolver's SignatureComparer which needs .Resolve()

namespace MonoMod.Packer.Utilities {
    public partial class SignatureComparer :
        IEqualityComparer<byte[]> {
        private const int ElementTypeOffset = 8;
        private const SignatureComparisonFlags DefaultFlags = SignatureComparisonFlags.VersionAgnostic;

        /// <summary>
        /// An immutable default instance of <see cref="SignatureComparer"/>.
        /// </summary>
        public static SignatureComparer Default { get; } = new();

        /// <summary>
        /// Flags for controlling comparison behavior.
        /// </summary>
        public SignatureComparisonFlags Flags { get; }

        /// <summary>
        /// The default <see cref="SignatureComparer"/> constructor.
        /// </summary>
        public SignatureComparer() {
            Flags = DefaultFlags;
        }

        /// <summary>
        /// A <see cref="SignatureComparer"/> constructor with a parameter for specifying the <see cref="Flags"/>
        /// used in comparisons.
        /// </summary>
        /// <param name="flags">The <see cref="Flags"/> used in comparisons.</param>
        public SignatureComparer(SignatureComparisonFlags flags) {
            Flags = flags;
        }

        /// <inheritdoc />
        public bool Equals(byte[]? x, byte[]? y) => ByteArrayEqualityComparer.Instance.Equals(x, y);

        /// <inheritdoc />
        public int GetHashCode(byte[] obj) => ByteArrayEqualityComparer.Instance.GetHashCode(obj);
    }

    public partial class SignatureComparer :
        IEqualityComparer<TypeSignature>,
        IEqualityComparer<CorLibTypeSignature>,
        IEqualityComparer<ByReferenceTypeSignature>,
        IEqualityComparer<PointerTypeSignature>,
        IEqualityComparer<SzArrayTypeSignature>,
        IEqualityComparer<PinnedTypeSignature>,
        IEqualityComparer<BoxedTypeSignature>,
        IEqualityComparer<TypeDefOrRefSignature>,
        IEqualityComparer<CustomModifierTypeSignature>,
        IEqualityComparer<GenericInstanceTypeSignature>,
        IEqualityComparer<GenericParameterSignature>,
        IEqualityComparer<ArrayTypeSignature>,
        IEqualityComparer<SentinelTypeSignature>,
        IEqualityComparer<FunctionPointerTypeSignature>,
        IEqualityComparer<IList<TypeSignature>>,
        IEqualityComparer<IEnumerable<TypeSignature>> {
        /// <inheritdoc />
        public bool Equals(TypeSignature? x, TypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            switch (x.ElementType) {
                case ElementType.ValueType:
                case ElementType.Class:
                    return Equals(x as TypeDefOrRefSignature, y as TypeDefOrRefSignature);
                case ElementType.CModReqD:
                case ElementType.CModOpt:
                    return Equals(x as CustomModifierTypeSignature, y as CustomModifierTypeSignature);
                case ElementType.GenericInst:
                    return Equals(x as GenericInstanceTypeSignature, y as GenericInstanceTypeSignature);
                case ElementType.Var:
                case ElementType.MVar:
                    return Equals(x as GenericParameterSignature, y as GenericParameterSignature);
                case ElementType.Ptr:
                    return Equals(x as PointerTypeSignature, y as PointerTypeSignature);
                case ElementType.ByRef:
                    return Equals(x as ByReferenceTypeSignature, y as ByReferenceTypeSignature);
                case ElementType.Array:
                    return Equals(x as ArrayTypeSignature, y as ArrayTypeSignature);
                case ElementType.SzArray:
                    return Equals(x as SzArrayTypeSignature, y as SzArrayTypeSignature);
                case ElementType.Sentinel:
                    return Equals(x as SentinelTypeSignature, y as SentinelTypeSignature);
                case ElementType.Pinned:
                    return Equals(x as PinnedTypeSignature, y as PinnedTypeSignature);
                case ElementType.Boxed:
                    return Equals(x as BoxedTypeSignature, y as BoxedTypeSignature);
                case ElementType.FnPtr:
                    return Equals(x as FunctionPointerTypeSignature, y as FunctionPointerTypeSignature);
                case ElementType.Internal:
                case ElementType.Modifier:
                    throw new NotSupportedException();
                default:
                    return Equals(x as CorLibTypeSignature, y as CorLibTypeSignature);
            }
        }

        /// <inheritdoc />
        public int GetHashCode(TypeSignature obj) {
            switch (obj.ElementType) {
                case ElementType.ValueType:
                case ElementType.Class:
                    return GetHashCode((TypeDefOrRefSignature) obj);
                case ElementType.CModReqD:
                case ElementType.CModOpt:
                    return GetHashCode((CustomModifierTypeSignature) obj);
                case ElementType.GenericInst:
                    return GetHashCode((GenericInstanceTypeSignature) obj);
                case ElementType.Var:
                case ElementType.MVar:
                    return GetHashCode((GenericParameterSignature) obj);
                case ElementType.Ptr:
                    return GetHashCode((PointerTypeSignature) obj);
                case ElementType.ByRef:
                    return GetHashCode((ByReferenceTypeSignature) obj);
                case ElementType.Array:
                    return GetHashCode((ArrayTypeSignature) obj);
                case ElementType.SzArray:
                    return GetHashCode((SzArrayTypeSignature) obj);
                case ElementType.Sentinel:
                    return GetHashCode((SentinelTypeSignature) obj);
                case ElementType.Pinned:
                    return GetHashCode((PinnedTypeSignature) obj);
                case ElementType.Boxed:
                    return GetHashCode((BoxedTypeSignature) obj);
                case ElementType.FnPtr:
                case ElementType.Internal:
                case ElementType.Modifier:
                    throw new NotSupportedException();
                default:
                    return GetHashCode((CorLibTypeSignature) obj);
            }
        }

        /// <inheritdoc />
        public bool Equals(CorLibTypeSignature? x, CorLibTypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return x.ElementType == y.ElementType;
        }

        /// <inheritdoc />
        public int GetHashCode(CorLibTypeSignature obj) =>
            (int) obj.ElementType << ElementTypeOffset;

        /// <inheritdoc />
        public bool Equals(SentinelTypeSignature? x, SentinelTypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return x.ElementType == y.ElementType;
        }

        /// <inheritdoc />
        public int GetHashCode(SentinelTypeSignature obj) =>
            (int) obj.ElementType << ElementTypeOffset;

        /// <inheritdoc />
        public bool Equals(ByReferenceTypeSignature? x, ByReferenceTypeSignature? y) =>
            Equals(x as TypeSpecificationSignature, y);

        /// <inheritdoc />
        public int GetHashCode(ByReferenceTypeSignature obj) =>
            GetHashCode(obj as TypeSpecificationSignature);

        /// <inheritdoc />
        public bool Equals(PointerTypeSignature? x, PointerTypeSignature? y) =>
            Equals(x as TypeSpecificationSignature, y);

        /// <inheritdoc />
        public int GetHashCode(PointerTypeSignature obj) =>
            GetHashCode(obj as TypeSpecificationSignature);

        /// <inheritdoc />
        public bool Equals(SzArrayTypeSignature? x, SzArrayTypeSignature? y) =>
            Equals(x as TypeSpecificationSignature, y);

        /// <inheritdoc />
        public int GetHashCode(SzArrayTypeSignature obj) =>
            GetHashCode(obj as TypeSpecificationSignature);

        /// <inheritdoc />
        public bool Equals(PinnedTypeSignature? x, PinnedTypeSignature? y) =>
            Equals(x as TypeSpecificationSignature, y);

        /// <inheritdoc />
        public int GetHashCode(PinnedTypeSignature obj) =>
            GetHashCode(obj as TypeSpecificationSignature);

        /// <inheritdoc />
        public bool Equals(BoxedTypeSignature? x, BoxedTypeSignature? y) =>
            Equals(x as TypeSpecificationSignature, y);

        /// <inheritdoc />
        public int GetHashCode(BoxedTypeSignature obj) =>
            GetHashCode(obj as TypeSpecificationSignature);

        /// <inheritdoc />
        public bool Equals(TypeDefOrRefSignature? x, TypeDefOrRefSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return SimpleTypeEquals(x.Type, y.Type);
        }

        /// <inheritdoc />
        public int GetHashCode(TypeDefOrRefSignature obj) => SimpleTypeHashCode(obj);

        /// <inheritdoc />
        public bool Equals(CustomModifierTypeSignature? x, CustomModifierTypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.IsRequired == y.IsRequired
                   && Equals(x.ModifierType, y.ModifierType)
                   && Equals(x.BaseType, y.BaseType);
        }

        /// <inheritdoc />
        public int GetHashCode(CustomModifierTypeSignature obj) {
            unchecked {
                var hashCode = (int) obj.ElementType << ElementTypeOffset;
                hashCode = (hashCode * 397) ^ obj.ModifierType.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.BaseType.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(GenericInstanceTypeSignature? x, GenericInstanceTypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.IsValueType == y.IsValueType
                   && Equals(x.GenericType, y.GenericType)
                   && Equals(x.TypeArguments, y.TypeArguments);
        }

        /// <inheritdoc />
        public int GetHashCode(GenericInstanceTypeSignature obj) {
            unchecked {
                var hashCode = (int) obj.ElementType << ElementTypeOffset;
                hashCode = (hashCode * 397) ^ obj.GenericType.GetHashCode();
                hashCode = (hashCode * 397) ^ GetHashCode(obj.TypeArguments);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(GenericParameterSignature? x, GenericParameterSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Index == y.Index
                   && x.ParameterType == y.ParameterType;
        }

        /// <inheritdoc />
        public int GetHashCode(GenericParameterSignature obj) =>
            (int) obj.ElementType << ElementTypeOffset | obj.Index;

        private bool Equals(TypeSpecificationSignature? x, TypeSpecificationSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.ElementType != y.ElementType)
                return false;
            return Equals(x.BaseType, y.BaseType);
        }

        private int GetHashCode(TypeSpecificationSignature obj) => SimpleTypeSpecHashCode(obj);

        private int SimpleTypeSpecHashCode(TypeSpecificationSignature obj) {
            return (int) obj.ElementType << ElementTypeOffset ^ GetHashCode(obj.BaseType);
        }

        /// <inheritdoc />
        public bool Equals(ArrayTypeSignature? x, ArrayTypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Dimensions.Count != y.Dimensions.Count)
                return false;

            for (var i = 0; i < x.Dimensions.Count; i++) {
                if (x.Dimensions[i].Size != y.Dimensions[i].Size
                    || x.Dimensions[i].LowerBound != y.Dimensions[i].LowerBound) {
                    return false;
                }
            }

            return Equals(x.BaseType, y.BaseType);
        }

        /// <inheritdoc />
        public int GetHashCode(ArrayTypeSignature obj) {
            unchecked {
                var hashCode = (int) obj.ElementType << ElementTypeOffset;
                hashCode = (hashCode * 397) ^ GetHashCode(obj.BaseType);
                for (var i = 0; i < obj.Dimensions.Count; i++)
                    hashCode = (hashCode * 397) ^ obj.Dimensions[i].GetHashCode();

                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(FunctionPointerTypeSignature? x, FunctionPointerTypeSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return Equals(x.Signature, y.Signature);
        }

        /// <inheritdoc />
        public int GetHashCode(FunctionPointerTypeSignature obj) {
            return obj.Signature.GetHashCode();
        }

        /// <inheritdoc />
        public bool Equals(IList<TypeSignature>? x, IList<TypeSignature>? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Count != y.Count)
                return false;

            for (var i = 0; i < x.Count; i++) {
                if (!Equals(x[i], y[i]))
                    return false;
            }

            return true;
        }

        /// <inheritdoc />
        public int GetHashCode(IList<TypeSignature> obj) {
            var checksum = 0;
            for (var i = 0; i < obj.Count; i++)
                checksum ^= GetHashCode(obj[i]);
            return checksum;
        }

        /// <inheritdoc />
        public bool Equals(IEnumerable<TypeSignature>? x, IEnumerable<TypeSignature>? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.SequenceEqual(y, this);
        }

        /// <inheritdoc />
        public int GetHashCode(IEnumerable<TypeSignature> obj) {
            var checksum = 0;
            foreach (var type in obj)
                checksum ^= GetHashCode(type);
            return checksum;
        }
    }
    public partial class SignatureComparer :
        IEqualityComparer<ITypeDescriptor>,
        IEqualityComparer<ITypeDefOrRef>,
        IEqualityComparer<TypeDefinition>,
        IEqualityComparer<TypeReference>,
        IEqualityComparer<TypeSpecification>,
        IEqualityComparer<ExportedType>,
        IEqualityComparer<InvalidTypeDefOrRef> {
        /// <inheritdoc />
        public bool Equals(ITypeDescriptor? x, ITypeDescriptor? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x switch {
                InvalidTypeDefOrRef invalidType => Equals(invalidType, y as InvalidTypeDefOrRef),
                TypeSpecification specification => Equals(specification, y as TypeSpecification),
                TypeSignature signature => Equals(signature, y as TypeSignature),
                _ => SimpleTypeEquals(x, y)
            };
        }

        /// <inheritdoc />
        public int GetHashCode(ITypeDescriptor obj) => obj switch {
            InvalidTypeDefOrRef invalidType => GetHashCode(invalidType),
            ITypeDefOrRef typeDefOrRef => GetHashCode(typeDefOrRef),
            TypeSignature signature => GetHashCode(signature),
            _ => SimpleTypeHashCode(obj)
        };

        protected virtual int SimpleTypeHashCode(ITypeDescriptor obj) {
            unchecked {
                var hashCode = obj.Name?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (obj.Namespace?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (obj.DeclaringType is null ? 0 : GetHashCode(obj.DeclaringType));
                return hashCode;
            }
        }

        protected virtual bool SimpleTypeEquals(ITypeDescriptor x, ITypeDescriptor y) {
            // Check the basic properties first.
            if (!x.IsTypeOf(y.Namespace, y.Name))
                return false;

            // If scope matches, it is a perfect match.
            if (Equals(x.Scope, y.Scope))
                return true;

            // It can still be an exported type, we need to resolve the type then and check if the definitions match.
            if (!Equals(x.Module, y.Module)) {
                return x.Resolve() is { } definition1
                       && y.Resolve() is { } definition2
                       && Equals(definition1.Module!.Assembly, definition2.Module!.Assembly)
                       && Equals(definition1.DeclaringType, definition2.DeclaringType);
            }

            return false;
        }

        /// <inheritdoc />
        public bool Equals(ITypeDefOrRef? x, ITypeDefOrRef? y) => Equals(x as ITypeDescriptor, y);

        /// <inheritdoc />
        public int GetHashCode(ITypeDefOrRef obj) => obj.MetadataToken.Table == TableIndex.TypeSpec
            ? GetHashCode((TypeSpecification) obj)
            : SimpleTypeHashCode(obj);

        /// <inheritdoc />
        public bool Equals(TypeDefinition? x, TypeDefinition? y) => Equals(x as ITypeDescriptor, y);

        /// <inheritdoc />
        public int GetHashCode(TypeDefinition obj) => SimpleTypeHashCode(obj);

        /// <inheritdoc />
        public bool Equals(TypeReference? x, TypeReference? y) => Equals(x as ITypeDescriptor, y);

        /// <inheritdoc />
        public int GetHashCode(TypeReference obj) => SimpleTypeHashCode(obj);

        /// <inheritdoc />
        public bool Equals(TypeSpecification? x, TypeSpecification? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return Equals(x.Signature, y.Signature);
        }

        /// <inheritdoc />
        public int GetHashCode(TypeSpecification obj) => obj.Signature is not null ? GetHashCode(obj.Signature) : 0;

        /// <inheritdoc />
        public bool Equals(ExportedType? x, ExportedType? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return Equals((ITypeDescriptor) x, y);
        }

        /// <inheritdoc />
        public int GetHashCode(ExportedType obj) => GetHashCode((ITypeDescriptor) obj);

        /// <inheritdoc />
        public bool Equals(InvalidTypeDefOrRef? x, InvalidTypeDefOrRef? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Error == y.Error;
        }

        /// <inheritdoc />
        public int GetHashCode(InvalidTypeDefOrRef obj) => (int) obj.Error;
    }

    public partial class SignatureComparer :
        IEqualityComparer<MemberReference>,
        IEqualityComparer<IMethodDescriptor>,
        IEqualityComparer<IFieldDescriptor>,
        IEqualityComparer<MethodSpecification> {
        /// <inheritdoc />
        public bool Equals(MemberReference? x, MemberReference? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            if (x.IsMethod)
                return Equals((IMethodDescriptor) x, y);
            if (y.IsField)
                return Equals((IFieldDescriptor) x, y);
            return false;
        }

        /// <inheritdoc />
        public int GetHashCode(MemberReference obj) {
            if (obj.IsMethod)
                return GetHashCode((IMethodDescriptor) obj);
            if (obj.IsField)
                return GetHashCode((IFieldDescriptor) obj);
            throw new ArgumentOutOfRangeException(nameof(obj));
        }

        /// <inheritdoc />
        public bool Equals(IMethodDescriptor? x, IMethodDescriptor? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            if (x is MethodSpecification specification)
                return Equals(specification, y as MethodSpecification);

            return x.Name == y.Name
                   && Equals(x.DeclaringType, y.DeclaringType)
                   && Equals(x.Signature, y.Signature);
        }

        /// <inheritdoc />
        public int GetHashCode(IMethodDescriptor obj) {
            unchecked {
                var hashCode = obj.Name is null ? 0 : obj.Name.GetHashCode();
                hashCode = (hashCode * 397) ^ (obj.DeclaringType is not null ? GetHashCode(obj.DeclaringType) : 0);
                hashCode = (hashCode * 397) ^ (obj.Signature is not null ? GetHashCode(obj.Signature) : 0);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(IFieldDescriptor? x, IFieldDescriptor? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Name == y.Name
                   && Equals(x.DeclaringType, y.DeclaringType)
                   && Equals(x.Signature, y.Signature);
        }

        /// <inheritdoc />
        public int GetHashCode(IFieldDescriptor obj) {
            unchecked {
                var hashCode = obj.Name is null ? 0 : obj.Name.GetHashCode();
                hashCode = (hashCode * 397) ^ (obj.DeclaringType is not null ? GetHashCode(obj.DeclaringType) : 0);
                hashCode = (hashCode * 397) ^ (obj.Signature is not null ? GetHashCode(obj.Signature) : 0);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(MethodSpecification? x, MethodSpecification? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return Equals(x.Method, y.Method)
                   && Equals(x.Signature, y.Signature);
        }

        /// <inheritdoc />
        public int GetHashCode(MethodSpecification obj) {
            unchecked {
                var hashCode = obj.Method == null ? 0 : GetHashCode(obj.Method);
                hashCode = (hashCode * 397) ^ (obj.Signature is not null ? GetHashCode(obj.Signature) : 0);
                return hashCode;
            }
        }
    }

    public partial class SignatureComparer :
        IEqualityComparer<CallingConventionSignature>,
        IEqualityComparer<FieldSignature>,
        IEqualityComparer<MethodSignature>,
        IEqualityComparer<PropertySignature>,
        IEqualityComparer<LocalVariablesSignature>,
        IEqualityComparer<GenericInstanceMethodSignature> {
        /// <inheritdoc />
        public bool Equals(CallingConventionSignature? x, CallingConventionSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x switch {
                LocalVariablesSignature localVarSig => Equals(localVarSig, y as LocalVariablesSignature),
                FieldSignature fieldSig => Equals(fieldSig, y as FieldSignature),
                MethodSignature methodSig => Equals(methodSig, y as MethodSignature),
                PropertySignature propertySig => Equals(propertySig, y as PropertySignature),
                _ => false
            };
        }

        /// <inheritdoc />
        public int GetHashCode(CallingConventionSignature obj) {
            return obj switch {
                LocalVariablesSignature localVarSig => GetHashCode(localVarSig),
                FieldSignature fieldSig => GetHashCode(fieldSig),
                MethodSignature methodSig => GetHashCode(methodSig),
                PropertySignature propertySig => GetHashCode(propertySig),
                _ => throw new ArgumentOutOfRangeException(nameof(obj))
            };
        }

        /// <inheritdoc />
        public bool Equals(FieldSignature? x, FieldSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Attributes == y.Attributes
                   && Equals(x.FieldType, y.FieldType);
        }

        /// <inheritdoc />
        public int GetHashCode(FieldSignature obj) {
            unchecked {
                var hashCode = (int) obj.Attributes;
                hashCode = (hashCode * 397) ^ GetHashCode(obj.FieldType);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(MethodSignature? x, MethodSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Attributes == y.Attributes
                   && x.GenericParameterCount == y.GenericParameterCount
                   && Equals(x.ReturnType, y.ReturnType)
                   && Equals(x.ParameterTypes, y.ParameterTypes)
                   && Equals(x.SentinelParameterTypes, y.SentinelParameterTypes);
        }

        /// <inheritdoc />
        public int GetHashCode(MethodSignature obj) {
            unchecked {
                var hashCode = (int) obj.Attributes;
                hashCode = (hashCode * 397) ^ obj.GenericParameterCount;
                hashCode = (hashCode * 397) ^ GetHashCode(obj.ReturnType);
                hashCode = (hashCode * 397) ^ GetHashCode(obj.ParameterTypes);
                hashCode = (hashCode * 397) ^ GetHashCode(obj.SentinelParameterTypes);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(LocalVariablesSignature? x, LocalVariablesSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Attributes == y.Attributes
                   && Equals(x.VariableTypes, y.VariableTypes);
        }

        /// <inheritdoc />
        public int GetHashCode(LocalVariablesSignature obj) {
            unchecked {
                var hashCode = (int) obj.Attributes;
                hashCode = (hashCode * 397) ^ GetHashCode(obj.VariableTypes);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(GenericInstanceMethodSignature? x, GenericInstanceMethodSignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Attributes == y.Attributes
                   && Equals(x.TypeArguments, y.TypeArguments);
        }

        /// <inheritdoc />
        public int GetHashCode(GenericInstanceMethodSignature obj) {
            unchecked {
                var hashCode = (int) obj.Attributes;
                hashCode = (hashCode * 397) ^ GetHashCode(obj.TypeArguments);
                return hashCode;
            }
        }

        /// <inheritdoc />
        public bool Equals(PropertySignature? x, PropertySignature? y) {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;

            return x.Attributes == y.Attributes
                   && Equals(x.ReturnType, y.ReturnType)
                   && Equals(x.ParameterTypes, y.ParameterTypes);
        }

        /// <inheritdoc />
        public int GetHashCode(PropertySignature obj) {
            unchecked {
                var hashCode = (int) obj.Attributes;
                hashCode = (hashCode * 397) ^ GetHashCode(obj.ReturnType);
                hashCode = (hashCode * 397) ^ GetHashCode(obj.ParameterTypes);
                return hashCode;
            }
        }
    }
}
