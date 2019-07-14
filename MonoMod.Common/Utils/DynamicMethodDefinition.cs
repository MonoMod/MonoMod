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
using System.Security;
using System.Security.Permissions;
using System.Diagnostics.SymbolStore;

namespace MonoMod.Utils {
    public sealed partial class DynamicMethodDefinition : IDisposable {

        static DynamicMethodDefinition() {
            _InitReflEmit();
            _InitCopier();

            PreferRuntimeILCopy = Environment.GetEnvironmentVariable("MONOMOD_DMD_COPY") != "0";
        }

        private static readonly bool _IsMono = Type.GetType("Mono.Runtime") != null;
        private static readonly bool _IsNewMonoSRE = _IsMono && typeof(DynamicMethod).GetField("il_info", BindingFlags.NonPublic | BindingFlags.Instance) != null;
        private static readonly bool _IsOldMonoSRE = _IsMono && !_IsNewMonoSRE && typeof(DynamicMethod).GetField("ilgen", BindingFlags.NonPublic | BindingFlags.Instance) != null;

        private static bool _PreferCecil;

        private static readonly ConstructorInfo c_DebuggableAttribute = typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });
        private static readonly ConstructorInfo c_UnverifiableCodeAttribute = typeof(UnverifiableCodeAttribute).GetConstructor(new Type[] { });
        private static readonly ConstructorInfo c_IgnoresAccessChecksToAttribute = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor(new Type[] { typeof(string) });

        private static readonly FieldInfo f_mono_assembly = typeof(Assembly).GetField("_mono_assembly", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly Dictionary<string, AssemblyDefinition> _DynModuleCache = new Dictionary<string, AssemblyDefinition>();
        private static readonly Dictionary<Module, ModuleDefinition> _DynModuleReflCache = new Dictionary<Module, ModuleDefinition>();
        private static readonly Dictionary<Module, ModuleDefinition> _Modules = new Dictionary<Module, ModuleDefinition>();

        private static readonly Dictionary<Module, int> _ModuleRefs = new Dictionary<Module, int>();
        private Func<AssemblyName, ModuleDefinition> _ModuleGen;
        private ModuleDefinition _Module {
            get {
                if (_DynModuleDefinition != null)
                    return _DynModuleDefinition;
                if (_Modules.TryGetValue(Method.Module, out ModuleDefinition module))
                    return module;
                return null;
            }
            set {
                if (_DynModuleDefinition != null)
                    return;
                lock (_Modules) {
                    _Modules[Method.Module] = value;
                }
            }
        }
        private int _ModuleRef {
            get {
                if (_DynModuleDefinition != null)
                    return 0;
                if (_ModuleRefs.TryGetValue(Method.Module, out int refs))
                    return refs;
                return 0;
            }
            set {
                if (_DynModuleDefinition != null)
                    return;
                lock (_ModuleRefs) {
                    _ModuleRefs[Method.Module] = value;
                }
            }
        }

        public MethodBase Method { get; private set; }
        private MethodDefinition _Definition;
        public MethodDefinition Definition =>
            _DynModuleDefinition != null ? _Definition : (
                _Definition ??
                (_Definition = (_Module.LookupToken(Method.MetadataToken) as MethodReference)?.Resolve()?.Clone()) ??
                throw new InvalidOperationException("Method definition not found")
            );

        public static bool PreferRuntimeILCopy;
        public GeneratorType Generator = GeneratorType.Auto;
        public bool Debug = false;

        private ModuleDefinition _DynModuleDefinition;
        private bool _DynModuleIsPrivate;

        private Guid GUID = Guid.NewGuid();

        private bool _IsDisposed;

        internal DynamicMethodDefinition() {
            // If SRE has been stubbed out, prefer Cecil.
            _PreferCecil =
                (_IsMono && (
                    // Mono 4.X+
                    !_IsNewMonoSRE &&
                    // Unity pre 2018
                    !_IsOldMonoSRE
                )) ||
                
                (!_IsMono && (
                    // .NET
                    typeof(ILGenerator).Assembly
                    .GetType("System.Reflection.Emit.DynamicILGenerator")
                    ?.GetField("m_scope", BindingFlags.NonPublic | BindingFlags.Instance) == null
                )) ||
                
                false;

            string type = Environment.GetEnvironmentVariable("MONOMOD_DMD_TYPE");
            if (!string.IsNullOrEmpty(type)) {
                try {
                    // TryGet is unavailable.
                    Generator = (GeneratorType) Enum.Parse(typeof(GeneratorType), type, true);
                } catch {
                }
            }

            Debug = Environment.GetEnvironmentVariable("MONOMOD_DMD_DEBUG") == "1";
        }

        public DynamicMethodDefinition(MethodBase method, Func<AssemblyName, ModuleDefinition> moduleGen = null)
            : this() {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Reload(moduleGen);
        }

        public DynamicMethodDefinition(string name, Type returnType, Type[] parameterTypes)
            : this() {
            Method = null;

            _CreateDynModule(name, returnType, parameterTypes);
        }

        public ILProcessor GetILProcessor() {
            return Definition.Body.GetILProcessor();
        }

        public ILGenerator GetILGenerator() {
            return new Cil.CecilILGenerator(Definition.Body.GetILProcessor()).GetProxy();
        }

        private ModuleDefinition _CreateDynModule(string name, Type returnType, Type[] parameterTypes) {
            ModuleDefinition module = _DynModuleDefinition = ModuleDefinition.CreateModule($"DMD:DynModule<{name}>?{GetHashCode()}", new ModuleParameters() {
                Kind = ModuleKind.Dll,
                AssemblyResolver = new AssemblyCecilDefinitionResolver(_ModuleGen, new DefaultAssemblyResolver()),
#if !CECIL0_9
                ReflectionImporterProvider = new ReflectionCecilImporterProvider(null)
#endif
            });
            _DynModuleIsPrivate = true;

            TypeDefinition type = new TypeDefinition(
                "",
                $"DMD<{name}>?{GetHashCode()}",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class
            );
            module.Types.Add(type);

            MethodDefinition def = _Definition = new MethodDefinition(
                name,
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                returnType != null ? module.ImportReference(returnType) : module.TypeSystem.Void
            );
            foreach (Type paramType in parameterTypes)
                def.Parameters.Add(new ParameterDefinition(module.ImportReference(paramType)));
            type.Methods.Add(def);

            return module;
        }

        public void Reload(Func<AssemblyName, ModuleDefinition> moduleGen = null, bool forceModule = false) {
            if (Method == null)
                throw new InvalidOperationException();

            ModuleDefinition moduleTmp = null;

            if (moduleGen != null)
                _ModuleGen = moduleGen;

            try {
                _Definition = null;

                ModuleDefinition module = (moduleGen ?? _ModuleGen)?.Invoke(Method.Module.Assembly.GetName());
                lock (_ModuleRefs) {
                    if (module == null) {
                        if (_DynModuleDefinition == null && _Module != null && !forceModule) {
                            module = _Module;
                        } else {
#if !CECIL0_9
                            _Module?.Dispose();
#endif
                            _Module = null;
                            _DynModuleDefinition = null;
                        }

                        if (!PreferRuntimeILCopy) {
                            string location = Method.DeclaringType?.Assembly.Location;
                            if (!string.IsNullOrEmpty(location)) {
                                ReaderParameters rp = new ReaderParameters();
                                if (_ModuleGen != null) {
                                    rp.AssemblyResolver = new AssemblyCecilDefinitionResolver(_ModuleGen, rp.AssemblyResolver ?? new DefaultAssemblyResolver());
#if !CECIL0_9
                                    rp.ReflectionImporterProvider = new ReflectionCecilImporterProvider(rp.ReflectionImporterProvider);
#endif
                                }
                                try {
                                    module = moduleTmp = ModuleDefinition.ReadModule(location, rp);
                                } catch {
                                    _DisposeEarly(true);
                                    module = moduleTmp = null;
                                }
                            }
                        }

                        if (module == null) {
                            Type[] argTypes;
                            ParameterInfo[] args = Method.GetParameters();
                            int offs = 0;
                            if (!Method.IsStatic) {
                                offs++;
                                argTypes = new Type[args.Length + 1];
                                argTypes[0] = Method.GetThisParamType();
                            } else {
                                argTypes = new Type[args.Length];
                            }
                            for (int i = 0; i < args.Length; i++)
                                argTypes[i + offs] = args[i].ParameterType;
                            module = _CreateDynModule(Method.Name, (Method as MethodInfo)?.ReturnType, argTypes);

                            _CopyMethodToDefinition();
                        }

                    }

                    _Module = module;
                    _ModuleRef++;
                }
                _Definition = Definition;
            } catch {
                _DisposeEarly(false);
                throw;
            }

            bool _DisposeEarly(bool silent) {
                if (moduleTmp != null) {
                    lock (_ModuleRefs) {
#if !CECIL0_9
                        moduleTmp.Dispose();
#endif
                        _Module = null;
                        _ModuleRef = 0;
                    }
                }
                return silent;
            }
        }

        public MethodInfo Generate()
            => Generate(null);
        public MethodInfo Generate(object context) {
            switch (Generator) {
                case GeneratorType.DynamicMethod:
                    return GenerateViaDynamicMethod();

#if !NETSTANDARD
                case GeneratorType.MethodBuilder:
                    return GenerateViaMethodBuilder(context as TypeBuilder);
#endif

                case GeneratorType.Cecil:
                    return GenerateViaCecil(context as TypeDefinition);

                default:
                    if (_PreferCecil)
                        return GenerateViaCecil(context as TypeDefinition);

                    if (Debug)
#if NETSTANDARD
                        return GenerateViaCecil(context as TypeDefinition);
#else
                        return GenerateViaMethodBuilder(context as TypeBuilder);
#endif

                    // In .NET Framework, DynamicILGenerator doesn't support fault and filter blocks.
                    // This is a non-issue in .NET Core, yet it could still be an issue in mono.
                    // https://github.com/dotnet/coreclr/issues/1764
#if NETFRAMEWORK
                    if (Definition.Body.ExceptionHandlers.Any(eh =>
                        eh.HandlerType == ExceptionHandlerType.Fault ||
                        eh.HandlerType == ExceptionHandlerType.Filter
                    ))
#if NETSTANDARD
                        return GenerateViaCecil(context as TypeDefinition);
#else
                        return GenerateViaMethodBuilder(context as TypeBuilder);
#endif
#endif

                    return GenerateViaDynamicMethod();
            }
        }

        public void Dispose() {
            if (_IsDisposed)
                return;
            _IsDisposed = true;

            if (_DynModuleDefinition != null && !_DynModuleIsPrivate)
                return;

            lock (_ModuleRefs) {
                if (_Module != null && (--_ModuleRef) == 0) {
#if !CECIL0_9
                    _Module.Dispose();
#endif
                    _Module = null;
                }
            }
        }

        public string GetDumpName(string type) {
            // TODO: Add {Definition.GetFindableID(withType: false)} without killing MethodBuilder
            return $"DMDASM.{GUID.GetHashCode():X8}{(string.IsNullOrEmpty(type) ? "" : $".{type}")}";
        }

        private static unsafe MethodInfo _Postbuild(MethodInfo mi) {
            if (mi == null)
                return null;

            if (_IsMono) {
                if (!(mi is DynamicMethod) && mi.DeclaringType != null) {
                    // Mono doesn't know about IgnoresAccessChecksToAttribute,
                    // but it lets some assemblies have unrestricted access.

                    if (_IsOldMonoSRE) {
                        // If you're reading this:
                        // You really should've chosen the SRE backend instead...

                    } else {
                        // https://github.com/mono/mono/blob/df846bcbc9706e325f3b5dca4d09530b80e9db83/mono/metadata/metadata-internals.h#L207
                        // https://github.com/mono/mono/blob/1af992a5ffa46e20dd61a64b6dcecef0edb5c459/mono/metadata/appdomain.c#L1286
                        // https://github.com/mono/mono/blob/beb81d3deb068f03efa72be986c96f9c3ab66275/mono/metadata/class.c#L5748
                        IntPtr asmPtr = (IntPtr) f_mono_assembly.GetValue(mi.Module.Assembly);
                        int offs =
                            // ref_count (4 + padding)
                            IntPtr.Size +
                            // basedir
                            IntPtr.Size +

                            // aname
                            // name
                            IntPtr.Size +
                            // culture
                            IntPtr.Size +
                            // hash_value
                            IntPtr.Size +
                            // public_key
                            IntPtr.Size +
                            // public_key_token (17 + padding)
                            20 +
                            // hash_alg
                            4 +
                            // hash_len
                            4 +
                            // flags
                            4 +

                            // major, minor, build, revision, arch (10 framework / 20 core + padding)
                            (
                                typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ? (IntPtr.Size == 4 ? 20 : 24) :
                                (IntPtr.Size == 4 ? 12 : 16)
                            ) +

                            // image
                            IntPtr.Size +
                            // friend_assembly_names
                            IntPtr.Size +
                            // friend_assembly_names_inited
                            1 +
                            // in_gac
                            1 +
                            // dynamic
                            1;
                        byte* corlibInternalPtr = (byte*) ((long) asmPtr + offs);
                        *corlibInternalPtr = 1;
                    }
                }


            }

            return mi;
        }

        class AssemblyCecilDefinitionResolver : IAssemblyResolver {
            private readonly Func<AssemblyName, ModuleDefinition> Gen;
            private readonly IAssemblyResolver Fallback;
            private readonly Dictionary<string, AssemblyDefinition> Cache = new Dictionary<string, AssemblyDefinition>();

            public AssemblyCecilDefinitionResolver(Func<AssemblyName, ModuleDefinition> moduleGen, IAssemblyResolver fallback) {
                Gen = moduleGen;
                Fallback = fallback;
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name) {
                if (_DynModuleCache.TryGetValue(name.FullName, out AssemblyDefinition asm))
                    return asm;
                if (Cache.TryGetValue(name.FullName, out asm))
                    return asm;
                return Cache[name.FullName] = Gen(new AssemblyName(name.FullName))?.Assembly ?? Fallback.Resolve(name);
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
                if (_DynModuleCache.TryGetValue(name.FullName, out AssemblyDefinition asm))
                    return asm;
                if (Cache.TryGetValue(name.FullName, out asm))
                    return asm;
                return Cache[name.FullName] = Gen(new AssemblyName(name.FullName)).Assembly ?? Fallback.Resolve(name, parameters);
            }

#if CECIL0_9

            public AssemblyDefinition Resolve(string fullName) {
                if (_DynModuleCache.TryGetValue(fullName, out AssemblyDefinition asm))
                    return asm;
                if (Cache.TryGetValue(fullName, out asm))
                    return asm;
                return Cache[fullName] = Gen(new AssemblyName(fullName)).Assembly ?? Fallback.Resolve(fullName);
            }

            public AssemblyDefinition Resolve(string fullName, ReaderParameters parameters) {
                if (_DynModuleCache.TryGetValue(fullName, out AssemblyDefinition asm))
                    return asm;
                if (Cache.TryGetValue(fullName, out asm))
                    return asm;
                return Cache[fullName] = Gen(new AssemblyName(fullName)).Assembly ?? Fallback.Resolve(fullName, parameters);
            }

#else

            public void Dispose() {
                foreach (AssemblyDefinition asm in Cache.Values)
                    asm.Dispose();
                Cache.Clear();
            }

#endif
        }

#if !CECIL0_9
        class ReflectionCecilImporterProvider : IReflectionImporterProvider {
            private readonly IReflectionImporterProvider Fallback;

            public ReflectionCecilImporterProvider(IReflectionImporterProvider fallback) {
                Fallback = fallback;
            }

            public IReflectionImporter GetReflectionImporter(ModuleDefinition module) {
                return new ReflectionCecilImporter(
                    module,
                    Fallback?.GetReflectionImporter(module) ??
#if MONOMOD_UTILS
                    new MMReflectionImporter(module) {
                        UseDefault = false
                    }
#else
                    new DefaultReflectionImporter(module)
#endif
                );
            }
        }

        class ReflectionCecilImporter : IReflectionImporter {
            private readonly ModuleDefinition Module;
            private readonly IReflectionImporter Fallback;

            public ReflectionCecilImporter(ModuleDefinition module, IReflectionImporter fallback) {
                Module = module;
                Fallback = fallback;
            }

            public AssemblyNameReference ImportReference(AssemblyName reference) {
                return Fallback.ImportReference(reference);
            }

            public TypeReference ImportReference(Type type, IGenericParameterProvider context) {
                if (_DynModuleReflCache.TryGetValue(type.Module, out ModuleDefinition dynModule) && dynModule != Module)
                    return Module.ImportReference(dynModule.ImportReference(type, context).Resolve());
                return Fallback.ImportReference(type, context);
            }

            public FieldReference ImportReference(FieldInfo field, IGenericParameterProvider context) {
                if (_DynModuleReflCache.TryGetValue(field.DeclaringType.Module, out ModuleDefinition dynModule) && dynModule != Module)
                    return Module.ImportReference(dynModule.ImportReference(field, context).Resolve());
                return Fallback.ImportReference(field, context);
            }

            public MethodReference ImportReference(MethodBase method, IGenericParameterProvider context) {
                if (method is DynamicMethod dm)
                    return new DynamicMethodReference(Module, dm);
                if (_DynModuleReflCache.TryGetValue(method.DeclaringType.Module, out ModuleDefinition dynModule) && dynModule != Module)
                    return Module.ImportReference(dynModule.ImportReference(method, context).Resolve());
                return Fallback.ImportReference(method, context);
            }
        }
#endif

            class DynamicMethodReference : MethodReference {
            public DynamicMethod DynamicMethod;

            public DynamicMethodReference(ModuleDefinition module, DynamicMethod dm)
                : base("", module.TypeSystem.Void) {
                DynamicMethod = dm;
            }
        }

        public enum GeneratorType {
            Auto = 0,
            DynamicMethod = 1,
            DM = 1,
#if !NETSTANDARD
            MethodBuilder = 2,
            MB = 2,
#endif
            Cecil = 3,
            MC = 3,
        }

    }
}
