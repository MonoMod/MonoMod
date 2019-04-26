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
    public sealed class MMReflectionImporter : IReflectionImporter {

        private class _Provider : IReflectionImporterProvider {
            public IReflectionImporter GetReflectionImporter(ModuleDefinition module) {
                return new MMReflectionImporter(module);
            }
        }
        public static readonly IReflectionImporterProvider Provider = new _Provider();

        private readonly ModuleDefinition Module;
        private readonly DefaultReflectionImporter Default;

        public bool UseDefault =
#if NETSTANDARD
            false;
#else
            true;
#endif

        public MMReflectionImporter(ModuleDefinition module) {
            Module = module;
            Default = new DefaultReflectionImporter(module);
        }

        public AssemblyNameReference ImportReference(AssemblyName reference) {
            return Default.ImportReference(reference);
        }

        public TypeReference ImportReference(Type type, IGenericParameterProvider context) {
            return Default.ImportReference(type, context);
        }

        public FieldReference ImportReference(FieldInfo field, IGenericParameterProvider context) {
            if (UseDefault)
                return Default.ImportReference(field, context);

            TypeReference declaringType = ImportReference(field.DeclaringType, context);
            return new FieldReference(
                field.Name,
                ImportReference(field.FieldType, declaringType),
                declaringType
            );
        }

        public MethodReference ImportReference(MethodBase method, IGenericParameterProvider context) {
            if (UseDefault)
                return Default.ImportReference(method, context);

            if (method.IsGenericMethod) {
                GenericInstanceMethod gim = new GenericInstanceMethod(ImportReference((method as MethodInfo).GetGenericMethodDefinition(), context));
                foreach (Type arg in method.GetGenericArguments())
                    // Generic arguments for the generic instance are often given by the next higher provider.
                    gim.GenericArguments.Add(ImportReference(arg, context));

                return gim;
            }

            MethodReference methodref = new MethodReference(
                method.Name,
                ImportReference(typeof(void), context),
                ImportReference(method.DeclaringType, context)
            );

            methodref.HasThis = (method.CallingConvention & CallingConventions.HasThis) != 0;
            methodref.ExplicitThis = (method.CallingConvention & CallingConventions.ExplicitThis) != 0;
            if ((method.CallingConvention & CallingConventions.VarArgs) != 0)
                methodref.CallingConvention = MethodCallingConvention.VarArg;
            
            if (method.IsGenericMethodDefinition)
                foreach (Type param in method.GetGenericArguments())
                    methodref.GenericParameters.Add(new GenericParameter(param.Name, methodref));

            methodref.ReturnType = ImportReference((method as MethodInfo)?.ReturnType ?? typeof(void), methodref);

            foreach (ParameterInfo param in method.GetParameters())
                methodref.Parameters.Add(new ParameterDefinition(
                    param.Name,
                    (Mono.Cecil.ParameterAttributes) param.Attributes,
                    ImportReference(param.ParameterType, methodref)
                ));

            return methodref;
        }

    }
}
