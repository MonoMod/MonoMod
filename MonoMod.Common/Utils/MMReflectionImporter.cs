#if !CECIL0_9
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Security;

namespace MonoMod.Utils {
#if !MONOMOD_INTERNAL
    public
#endif
    sealed class MMReflectionImporter : IReflectionImporter {

        private class _Provider : IReflectionImporterProvider {
            public bool? UseDefault;
            public IReflectionImporter GetReflectionImporter(ModuleDefinition module) {
                MMReflectionImporter importer = new MMReflectionImporter(module);
                if (UseDefault != null)
                    importer.UseDefault = UseDefault.Value;
                return importer;
            }
        }

        public static readonly IReflectionImporterProvider Provider = new _Provider();
        public static readonly IReflectionImporterProvider ProviderNoDefault = new _Provider() { UseDefault = false };

        private readonly ModuleDefinition Module;
        private readonly DefaultReflectionImporter Default;

        private readonly Dictionary<AssemblyName, AssemblyNameReference> CachedAsms = new Dictionary<AssemblyName, AssemblyNameReference>();
        private readonly Dictionary<Module, TypeReference> CachedModuleTypes = new Dictionary<Module, TypeReference>();
        private readonly Dictionary<Type, TypeReference> CachedTypes = new Dictionary<Type, TypeReference>();
        private readonly Dictionary<FieldInfo, FieldReference> CachedFields = new Dictionary<FieldInfo, FieldReference>();
        private readonly Dictionary<MethodBase, MethodReference> CachedMethods = new Dictionary<MethodBase, MethodReference>();

        public bool UseDefault = false;

        private readonly Dictionary<Type, TypeReference> ElementTypes;

        public MMReflectionImporter(ModuleDefinition module) {
            Module = module;
            Default = new DefaultReflectionImporter(module);

            ElementTypes = new Dictionary<Type, TypeReference>() {
                { typeof(void), module.TypeSystem.Void },
                { typeof(bool), module.TypeSystem.Boolean },
                { typeof(char), module.TypeSystem.Char },
                { typeof(sbyte), module.TypeSystem.SByte },
                { typeof(byte), module.TypeSystem.Byte },
                { typeof(short), module.TypeSystem.Int16 },
                { typeof(ushort), module.TypeSystem.UInt16 },
                { typeof(int), module.TypeSystem.Int32 },
                { typeof(uint), module.TypeSystem.UInt32 },
                { typeof(long), module.TypeSystem.Int64 },
                { typeof(ulong), module.TypeSystem.UInt64 },
                { typeof(float), module.TypeSystem.Single },
                { typeof(double), module.TypeSystem.Double },
                { typeof(string), module.TypeSystem.String },
                { typeof(TypedReference), module.TypeSystem.TypedReference },
                { typeof(IntPtr), module.TypeSystem.IntPtr },
                { typeof(UIntPtr), module.TypeSystem.UIntPtr },
                { typeof(object), module.TypeSystem.Object },
            };
        }

        public AssemblyNameReference ImportReference(AssemblyName asm) {
            if (CachedAsms.TryGetValue(asm, out AssemblyNameReference asmRef))
                return asmRef;

            return CachedAsms[asm] = Default.ImportReference(asm);
        }

        public TypeReference ImportModuleType(Module module, IGenericParameterProvider context) {
            if (CachedModuleTypes.TryGetValue(module, out TypeReference typeRef))
                return typeRef;

            // See https://github.com/jbevain/cecil/blob/06da31930ff100cef48aef677c4ceeee858e6c04/Mono.Cecil/ModuleDefinition.cs#L1018
            return CachedModuleTypes[module] = new TypeReference(
                string.Empty,
                "<Module>",
                Module,
                ImportReference(module.Assembly.GetName())
            );
        }

        public TypeReference ImportReference(Type type, IGenericParameterProvider context) {
            if (CachedTypes.TryGetValue(type, out TypeReference typeRef))
                return typeRef;

            if (UseDefault)
                return CachedTypes[type] = Default.ImportReference(type, context);

            if (type.HasElementType) {
                if (type.IsByRef)
                    return CachedTypes[type] = new ByReferenceType(ImportReference(type.GetElementType(), context));

                if (type.IsPointer)
                    return CachedTypes[type] = new PointerType(ImportReference(type.GetElementType(), context));

                if (type.IsArray) {
                    ArrayType at = new ArrayType(ImportReference(type.GetElementType(), context), type.GetArrayRank());
                    if (type != type.GetElementType().MakeArrayType()) {
                        // Non-SzArray
                        // TODO: Find a way to get the bounds without instantiating the array type!
                        /*
                        Array a = Array.CreateInstance(type, new int[type.GetArrayRank()]);
                        if (
                            at.Rank > 1
                            && a.IsFixedSize
                        ) {
                            for (int i = 0; i < at.Rank; i++)
                                at.Dimensions[i] = new ArrayDimension(a.GetLowerBound(i), a.GetUpperBound(i));
                        }
                        */
                        // For now, always assume [0...,0...,
                        // Array.CreateInstance only accepts lower bounds anyway.
                        for (int i = 0; i < at.Rank; i++)
                            at.Dimensions[i] = new ArrayDimension(0, null);
                    }
                    return CachedTypes[type] = at;
                }
            }

            bool isGeneric = type.IsGenericType;
            if (isGeneric && !type.IsGenericTypeDefinition) {
                GenericInstanceType git = new GenericInstanceType(ImportReference(type.GetGenericTypeDefinition(), context));
                foreach (Type arg in type.GetGenericArguments())
                    git.GenericArguments.Add(ImportReference(arg, context));
                return git;
            }

            if (type.IsGenericParameter)
                return CachedTypes[type] = ImportGenericParameter(type, context);

            if (ElementTypes.TryGetValue(type, out typeRef))
                return CachedTypes[type] = typeRef;

            typeRef = new TypeReference(
				string.Empty,
				type.Name,
				Module,
				ImportReference(type.Assembly.GetName()),
                type.IsValueType
            );

            if (type.IsNested)
                typeRef.DeclaringType = ImportReference(type.DeclaringType, context);
            else if (type.Namespace != null)
                typeRef.Namespace = type.Namespace;

            if (type.IsGenericType)
                foreach (Type param in type.GetGenericArguments())
                    typeRef.GenericParameters.Add(new GenericParameter(param.Name, typeRef));

            return CachedTypes[type] = typeRef;
        }

        private static TypeReference ImportGenericParameter(Type type, IGenericParameterProvider context) {
            if (context is MethodReference ctxMethodRef) {
                MethodBase dclMethod = type.DeclaringMethod;
                if (dclMethod != null) {
                    return ctxMethodRef.GenericParameters[type.GenericParameterPosition];
                } else {
                    context = ctxMethodRef.DeclaringType;
                }
            }

            Type dclType = type.DeclaringType;
            if (dclType == null)
                throw new InvalidOperationException();

            if (context is TypeReference ctxTypeRef) {
                while (ctxTypeRef != null) {
                    TypeReference ctxTypeRefEl = ctxTypeRef.GetElementType();
                    if (ctxTypeRefEl.Is(dclType))
                        return ctxTypeRefEl.GenericParameters[type.GenericParameterPosition];

                    if (ctxTypeRef.Is(dclType))
                        return ctxTypeRef.GenericParameters[type.GenericParameterPosition];

                    ctxTypeRef = ctxTypeRef.DeclaringType;
                    continue;
                }
            }

            throw new NotSupportedException();
        }

        public FieldReference ImportReference(FieldInfo field, IGenericParameterProvider context) {
            if (CachedFields.TryGetValue(field, out FieldReference fieldRef))
                return fieldRef;

            if (UseDefault)
                return CachedFields[field] = Default.ImportReference(field, context);

            Type declType = field.DeclaringType;
            TypeReference declaringType = declType != null ? ImportReference(declType, context) : ImportModuleType(field.Module, context);

            FieldInfo fieldOrig = field;
            if (declType != null && declType.IsGenericType) {
                // In methods of generic types, all generic parameters are already filled in.
                // Meanwhile, cecil requires generic parameter references.
                // Luckily the metadata tokens match up.
                field = field.Module.ResolveField(field.MetadataToken);
            }

            return CachedFields[fieldOrig] = new FieldReference(
                field.Name,
                ImportReference(field.FieldType, declaringType),
                declaringType
            );
        }

        public MethodReference ImportReference(MethodBase method, IGenericParameterProvider context) {
            if (CachedMethods.TryGetValue(method, out MethodReference methodRef))
                return methodRef;

            if (method is DynamicMethod dm)
                return new DynamicMethodReference(Module, dm);

            if (UseDefault)
                return CachedMethods[method] = Default.ImportReference(method, context);

            if (method.IsGenericMethod && !method.IsGenericMethodDefinition) {
                GenericInstanceMethod gim = new GenericInstanceMethod(ImportReference((method as MethodInfo).GetGenericMethodDefinition(), context));
                foreach (Type arg in method.GetGenericArguments())
                    // Generic arguments for the generic instance are often given by the next higher provider.
                    gim.GenericArguments.Add(ImportReference(arg, context));

                return CachedMethods[method] = gim;
            }

            Type declType = method.DeclaringType;
            methodRef = new MethodReference(
                method.Name,
                ImportReference(typeof(void), context),
                declType != null ? ImportReference(declType, context) : ImportModuleType(method.Module, context)
            );

            methodRef.HasThis = (method.CallingConvention & CallingConventions.HasThis) != 0;
            methodRef.ExplicitThis = (method.CallingConvention & CallingConventions.ExplicitThis) != 0;
            if ((method.CallingConvention & CallingConventions.VarArgs) != 0)
                methodRef.CallingConvention = MethodCallingConvention.VarArg;

            MethodBase methodOrig = method;
            if (declType != null && declType.IsGenericType) {
                // In methods of generic types, all generic parameters are already filled in.
                // Meanwhile, cecil requires generic parameter references.
                // Luckily the metadata tokens match up.
                method = method.Module.ResolveMethod(method.MetadataToken);
            }

            if (method.IsGenericMethodDefinition)
                foreach (Type param in method.GetGenericArguments())
                    methodRef.GenericParameters.Add(new GenericParameter(param.Name, methodRef));

            methodRef.ReturnType = ImportReference((method as MethodInfo)?.ReturnType ?? typeof(void), methodRef);

            foreach (ParameterInfo param in method.GetParameters())
                methodRef.Parameters.Add(new ParameterDefinition(
                    param.Name,
                    (Mono.Cecil.ParameterAttributes) param.Attributes,
                    ImportReference(param.ParameterType, methodRef)
                ));

            return CachedMethods[methodOrig] = methodRef;
        }

    }
}
#endif
