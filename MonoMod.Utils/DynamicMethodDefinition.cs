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
#if !NETSTANDARD
using System.Security.Permissions;
using System.Security;
using System.Diagnostics.SymbolStore;
#endif

namespace MonoMod.Utils {
    public sealed partial class DynamicMethodDefinition : IDisposable {

        static DynamicMethodDefinition() {
            _InitReflEmit();
        }

        private static readonly Dictionary<Module, ModuleDefinition> _Modules = new Dictionary<Module, ModuleDefinition>();
        private static readonly Dictionary<Module, int> _ModuleRefs = new Dictionary<Module, int>();
        private Func<AssemblyName, ModuleDefinition> _ModuleGen;
        private ModuleDefinition _Module {
            get {
                if (_Modules.TryGetValue(Method.Module, out ModuleDefinition module))
                    return module;
                return null;
            }
            set {
                lock (_Modules) {
                    _Modules[Method.Module] = value;
                }
            }
        }
        private int _ModuleRef {
            get {
                if (_ModuleRefs.TryGetValue(Method.Module, out int refs))
                    return refs;
                return 0;
            }
            set {
                lock (_ModuleRefs) {
                    _ModuleRefs[Method.Module] = value;
                }
            }
        }

        public MethodBase Method { get; private set; }
        private MethodDefinition _Definition;
        public MethodDefinition Definition =>
            _Definition ??
            (_Definition = (_Module.LookupToken(Method.GetMetadataToken()) as MethodReference)?.Resolve()?.Clone()) ??
            throw new InvalidOperationException("Method definition not found");

        public GeneratorType Generator = GeneratorType.Auto;
        public bool Debug = false;

        public DynamicMethodDefinition(MethodBase method, Func<AssemblyName, ModuleDefinition> moduleGen = null) {
            string type = Environment.GetEnvironmentVariable("MONOMOD_DMD_TYPE");
            if (!string.IsNullOrEmpty(type)) {
                try {
                    // TryGet is unavailable.
                    Generator = (GeneratorType) Enum.Parse(typeof(GeneratorType), type, true);
                } catch {
                }
            }
            Debug = Environment.GetEnvironmentVariable("MONOMOD_DMD_DEBUG") == "1";

            Method = method ?? throw new ArgumentNullException(nameof(method));
            Reload(moduleGen);
        }

        public void Reload(Func<AssemblyName, ModuleDefinition> moduleGen = null, bool forceModule = false) {
            ModuleDefinition moduleTmp = null;

            if (moduleGen != null)
                _ModuleGen = moduleGen;

            try {
                _Definition = null;
                ModuleDefinition module = (moduleGen ?? _ModuleGen)?.Invoke(Method.Module.Assembly.GetName());
                lock (_ModuleRefs) {
                    if (module == null) {
                        if (_Module != null && !forceModule) {
                            module = _Module;
                        } else {
#if !CECIL0_9
                            _Module?.Dispose();
#endif
                            _Module = null;
                        }
                        ReaderParameters rp = new ReaderParameters();
                        if (_ModuleGen != null)
                            rp.AssemblyResolver = new AssemblyCecilDefinitionResolver(_ModuleGen, rp.AssemblyResolver ?? new DefaultAssemblyResolver());
                        module = moduleTmp = ModuleDefinition.ReadModule(Method.DeclaringType.GetTypeInfo().Assembly.GetLocation(), rp);
                    }
                    _Module = module;
                    _ModuleRef++;
                }
                _Definition = Definition;
            } catch when (_DisposeEarly()) {
            }

            bool _DisposeEarly() {
                if (moduleTmp != null) {
                    lock (_ModuleRefs) {
#if !CECIL0_9
                        moduleTmp.Dispose();
#endif
                        _Module = null;
                        _ModuleRef = 0;
                    }
                }
                return false;
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
#if NETSTANDARD
                    // TODO: Automatically fall back to GenerateViaCecil
                    return GenerateViaDynamicMethod();
#else
                    if (Debug)
                        return GenerateViaMethodBuilder(context as TypeBuilder);
                    return GenerateViaDynamicMethod();
#endif
            }
        }

        private static void ResolveWithModifiers(TypeReference typeRef, out Type type, out Type[] typeModReq, out Type[] typeModOpt, List<Type> modReq = null, List<Type> modOpt = null) {
            if (modReq == null)
                modReq = new List<Type>();
            else
                modReq.Clear();

            if (modOpt == null)
                modOpt = new List<Type>();
            else
                modOpt.Clear();

            for (
                TypeReference mod = typeRef;
                mod is TypeSpecification modSpec;
                mod = modSpec.ElementType
            ) {
                switch (mod) {
                    case RequiredModifierType paramTypeModReq:
                        modReq.Add(paramTypeModReq.ModifierType.ResolveReflection());
                        break;

                    case OptionalModifierType paramTypeOptReq:
                        modOpt.Add(paramTypeOptReq.ModifierType.ResolveReflection());
                        break;
                }
            }

            type = typeRef.ResolveReflection();
            typeModReq = modReq.ToArray();
            typeModOpt = modOpt.ToArray();
        }

        public void Dispose() {
            lock (_ModuleRefs) {
                if (_Module != null && (--_ModuleRef) == 0) {
#if !CECIL0_9
                    _Module.Dispose();
#endif
                    _Module = null;
                }
            }
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
                if (Cache.TryGetValue(name.FullName, out AssemblyDefinition asm))
                    return asm;
                return Cache[name.FullName] = Gen(new AssemblyName(name.FullName))?.Assembly ?? Fallback.Resolve(name);
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
                if (Cache.TryGetValue(name.FullName, out AssemblyDefinition asm))
                    return asm;
                return Cache[name.FullName] = Gen(new AssemblyName(name.FullName)).Assembly ?? Fallback.Resolve(name, parameters);
            }

#if CECIL0_9

            public AssemblyDefinition Resolve(string fullName) {
                if (Cache.TryGetValue(fullName, out AssemblyDefinition asm))
                    return asm;
                return Cache[fullName] = Gen(new AssemblyName(fullName)).Assembly ?? Fallback.Resolve(fullName);
            }

            public AssemblyDefinition Resolve(string fullName, ReaderParameters parameters) {
                if (Cache.TryGetValue(fullName, out AssemblyDefinition asm))
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
