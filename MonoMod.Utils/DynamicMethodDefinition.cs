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

        private static readonly Dictionary<short, System.Reflection.Emit.OpCode> _ReflOpCodes = new Dictionary<short, System.Reflection.Emit.OpCode>();
        private static readonly Dictionary<short, Mono.Cecil.Cil.OpCode> _CecilOpCodes = new Dictionary<short, Mono.Cecil.Cil.OpCode>();
        private static readonly Dictionary<Type, MethodInfo> _Emitters = new Dictionary<Type, MethodInfo>();

#if !NETSTANDARD
        private static readonly ConstructorInfo c_DebuggableAttribute = typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });
        private static readonly ConstructorInfo c_UnverifiableCodeAttribute = typeof(UnverifiableCodeAttribute).GetConstructor(new Type[] { });
        private static readonly ConstructorInfo c_IgnoresAccessChecksToAttribute = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor(new Type[] { typeof(string) });
#endif

        static DynamicMethodDefinition() {
            foreach (FieldInfo field in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                System.Reflection.Emit.OpCode reflOpCode = (System.Reflection.Emit.OpCode) field.GetValue(null);
                _ReflOpCodes[reflOpCode.Value] = reflOpCode;
            }

            foreach (FieldInfo field in typeof(Mono.Cecil.Cil.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                Mono.Cecil.Cil.OpCode cecilOpCode = (Mono.Cecil.Cil.OpCode) field.GetValue(null);
                _CecilOpCodes[cecilOpCode.Value] = cecilOpCode;
            }

            foreach (MethodInfo method in typeof(ILGenerator).GetMethods()) {
                if (method.Name != "Emit")
                    continue;

                ParameterInfo[] args = method.GetParameters();
                if (args.Length != 2)
                    continue;

                if (args[0].ParameterType != typeof(System.Reflection.Emit.OpCode))
                    continue;
                _Emitters[args[1].ParameterType] = method;
            }
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

                default:
#if NETSTANDARD
                    return GenerateViaDynamicMethod();
#else
                    if (Debug)
                        return GenerateViaMethodBuilder(context as TypeBuilder);
                    return GenerateViaDynamicMethod();
#endif
            }
        }

        public DynamicMethod GenerateViaDynamicMethod() {
            ParameterInfo[] args = Method.GetParameters();
            Type[] argTypes;
            if (!Method.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = Method.GetThisParamType();
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            DynamicMethod dm = new DynamicMethod(
                $"DMD<{Method.GetFindableID(simple: true)}>",
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                Method.DeclaringType,
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = dm.GetILGenerator();

            _Generate(dm, il);

            return dm;
        }

#if !NETSTANDARD
        public MethodInfo GenerateViaMethodBuilder(TypeBuilder typeBuilder) {
            MethodBuilder method = GenerateMethodBuilder(typeBuilder);
            typeBuilder = (TypeBuilder) method.DeclaringType;
            Type type = typeBuilder.CreateType();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MONOMOD_DMD_DUMP"))) {
                string path = method.Module.FullyQualifiedName;
                string name = System.IO.Path.GetFileName(path);
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                ((AssemblyBuilder) typeBuilder.Assembly).Save(name);
            }
            return type.GetMethod(method.Name);
        }

        public MethodBuilder GenerateMethodBuilder(TypeBuilder typeBuilder) {
            if (typeBuilder == null) {
                string dumpDir = Environment.GetEnvironmentVariable("MONOMOD_DMD_DUMP");
                if (string.IsNullOrEmpty(dumpDir)) {
                    dumpDir = null;
                } else {
                    dumpDir = System.IO.Path.GetFullPath(dumpDir);
                }
                AssemblyBuilder ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = $"DMDASM_{GetHashCode()}"
                    },
                    AssemblyBuilderAccess.RunAndSave,
                    dumpDir
                );

                ab.SetCustomAttribute(new CustomAttributeBuilder(c_UnverifiableCodeAttribute, new object[] {
                }));

                if (Debug) {
                    ab.SetCustomAttribute(new CustomAttributeBuilder(c_DebuggableAttribute, new object[] {
                        DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.Default
                    }));
                }

                ModuleBuilder module = ab.DefineDynamicModule($"{ab.GetName().Name}.dll", $"{ab.GetName().Name}.dll", true);
                typeBuilder = module.DefineType(
                    $"DMD<{Method.GetFindableID(simple: true).Replace('.', '_')}>?{GetHashCode()}",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
            }

            ParameterInfo[] args = Method.GetParameters();
            Type[] argTypes;
            Type[][] argTypesModReq;
            Type[][] argTypesModOpt;
            if (!Method.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypesModReq = new Type[args.Length + 1][];
                argTypesModOpt = new Type[args.Length + 1][];
                argTypes[0] = Method.GetThisParamType();
                argTypesModReq[0] = Type.EmptyTypes;
                argTypesModOpt[0] = Type.EmptyTypes;
                for (int i = 0; i < args.Length; i++) {
                    argTypes[i + 1] = args[i].ParameterType;
                    argTypesModReq[i + 1] = args[i].GetRequiredCustomModifiers();
                    argTypesModOpt[i + 1] = args[i].GetOptionalCustomModifiers();
                }
            } else {
                argTypes = new Type[args.Length];
                argTypesModReq = new Type[args.Length][];
                argTypesModOpt = new Type[args.Length][];
                for (int i = 0; i < args.Length; i++) {
                    argTypes[i] = args[i].ParameterType;
                    argTypesModReq[i] = args[i].GetRequiredCustomModifiers();
                    argTypesModOpt[i] = args[i].GetOptionalCustomModifiers();
                }
            }

            // Required because the return type modifiers aren't easily accessible via reflection.
            ResolveWithModifiers(Definition.ReturnType, out Type returnType, out Type[] returnTypeModReq, out Type[] returnTypeModOpt);

            MethodBuilder mb = typeBuilder.DefineMethod(
                Method.Name.Replace('.', '_'),
                System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                CallingConventions.Standard,
                returnType, returnTypeModReq, returnTypeModOpt,
                argTypes, argTypesModReq, argTypesModOpt
            );
            ILGenerator il = mb.GetILGenerator();

            _Generate(mb, il);

            return mb;
        }
#endif

        private void _Generate(MethodBase _mb, ILGenerator il) {
            DynamicMethod dm = _mb as DynamicMethod;
#if !NETSTANDARD
            MethodBuilder mb = _mb as MethodBuilder;
            ModuleBuilder moduleBuilder = mb?.Module as ModuleBuilder;
            // moduleBuilder.Assembly sometimes avoids the .Assembly override under mysterious circumstances.
            AssemblyBuilder assemblyBuilder = (mb?.DeclaringType as TypeBuilder)?.Assembly as AssemblyBuilder;
            HashSet<Assembly> accessChecksIgnored = null;
            if (mb != null) {
                accessChecksIgnored = new HashSet<Assembly>();
            }
#endif

            MethodDefinition def = Definition;
#if !CECIL0_9
            MethodDebugInformation defInfo = Debug ? def.DebugInformation : null;
#endif

            LocalBuilder[] locals = def.Body.Variables.Select(
                var => {
                    LocalBuilder local = il.DeclareLocal(var.VariableType.ResolveReflection(), var.IsPinned);
#if !NETSTANDARD && !CECIL0_9
                    if (mb != null && defInfo != null && defInfo.TryGetName(var, out string name)) {
                        local.SetLocalSymInfo(name);
                    }
#endif
                    return local;
                }
            ).ToArray();

            // Pre-pass - Set up label map.
            Dictionary<Instruction, Label> labelMap = new Dictionary<Instruction, Label>();
            foreach (Instruction instr in def.Body.Instructions) {
                if (instr.Operand is Instruction[] targets) {
                    foreach (Instruction target in targets)
                        if (!labelMap.ContainsKey(target))
                            labelMap[target] = il.DefineLabel();

                } else if (instr.Operand is Instruction target) {
                    if (!labelMap.ContainsKey(target))
                        labelMap[target] = il.DefineLabel();
                }
            }

#if !NETSTANDARD && !CECIL0_9
            Dictionary<Document, ISymbolDocumentWriter> infoDocCache = mb == null ? null : new Dictionary<Document, ISymbolDocumentWriter>();
#endif

            int paramOffs = def.HasThis ? 1 : 0;
            object[] emitArgs = new object[2];
            foreach (Instruction instr in def.Body.Instructions) {
                if (labelMap.TryGetValue(instr, out Label label))
                    il.MarkLabel(label);

#if !NETSTANDARD && !CECIL0_9
                SequencePoint instrInfo = defInfo?.GetSequencePoint(instr);
                if (mb != null && instrInfo != null) {
                    if (!infoDocCache.TryGetValue(instrInfo.Document, out ISymbolDocumentWriter infoDoc)) {
                        infoDocCache[instrInfo.Document] = infoDoc = moduleBuilder.DefineDocument(
                            instrInfo.Document.Url,
                            instrInfo.Document.LanguageGuid,
                            instrInfo.Document.LanguageVendorGuid,
                            instrInfo.Document.TypeGuid
                        );
                    }
                    il.MarkSequencePoint(infoDoc, instrInfo.StartLine, instrInfo.StartColumn, instrInfo.EndLine, instrInfo.EndColumn);
                }
#endif

                // TODO: This can be improved perf-wise!
                foreach (ExceptionHandler handler in def.Body.ExceptionHandlers) {
                    if (handler.TryStart == instr) {
                        il.BeginExceptionBlock();

                    } else if (handler.FilterStart == instr) {
                        il.BeginExceptFilterBlock();

                    } else if (handler.HandlerStart == instr) {
                        switch (handler.HandlerType) {
                            case ExceptionHandlerType.Filter:
                                // Handled by FilterStart
                                break;
                            case ExceptionHandlerType.Catch:
                                il.BeginCatchBlock(handler.CatchType.ResolveReflection());
                                break;
                            case ExceptionHandlerType.Finally:
                                il.BeginFinallyBlock();
                                break;
                            case ExceptionHandlerType.Fault:
                                il.BeginFaultBlock();
                                break;
                        }
                    }
                }

                if (instr.OpCode.OperandType == Mono.Cecil.Cil.OperandType.InlineNone)
                    il.Emit(_ReflOpCodes[instr.OpCode.Value]);
                else {
                    object operand = instr.Operand;

                    if (operand is Instruction[] targets) {
                        operand = targets.Select(target => labelMap[target]).ToArray();
                        // Let's hope that the JIT treats the long forms identically to the short forms.
                        instr.OpCode = instr.OpCode.ShortToLongOp();
                    } else if (operand is Instruction target) {
                        operand = labelMap[target];
                        // Let's hope that the JIT treats the long forms identically to the short forms.
                        instr.OpCode = instr.OpCode.ShortToLongOp();
                    } else if (operand is VariableDefinition var) {
                        operand = locals[var.Index];
                    } else if (operand is ParameterDefinition param) {
                        operand = param.Index + paramOffs;
                    } else if (operand is MemberReference mref) {
                        MemberInfo member = mref.ResolveReflection();
                        operand = member;
#if !NETSTANDARD
                        // TODO: Only do the following for inaccessible members.
                        if (mb != null) {
                            Assembly asm = member.Module.Assembly;
                            if (!accessChecksIgnored.Contains(asm)) {
                                // while (member.DeclaringType != null)
                                //     member = member.DeclaringType;
                                assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(c_IgnoresAccessChecksToAttribute, new object[] {
                                    asm.GetName().Name
                                }));
                                accessChecksIgnored.Add(asm);
                            }
                        }
#endif
                    } else if (operand is CallSite csite) {
                        if (dm != null) {
                            // SignatureHelper in unmanaged contexts cannot be fully made use of for DynamicMethods.
                            EmitCallSite(dm, il, _ReflOpCodes[instr.OpCode.Value], csite);
                            continue;
                        }
#if !NETSTANDARD
                        operand = csite.ResolveReflection(mb.Module);
#else
                        throw new NotSupportedException();
#endif
                    }

#if !NETSTANDARD
                    if (mb != null && operand is MethodBase called && called.DeclaringType == null) {
                        // "Global" methods (f.e. DynamicMethods) cannot be tokenized.
                        if (instr.OpCode == Mono.Cecil.Cil.OpCodes.Call) {
                            if (operand is DynamicMethod target) {
                                // This should be heavily optimizable.
                                operand = CreateMethodProxy(mb, target);

                            } else {
                                IntPtr ptr = called.GetLdftnPointer();
                                if (IntPtr.Size == 4)
                                    il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, (int) ptr);
                                else
                                    il.Emit(System.Reflection.Emit.OpCodes.Ldc_I8, (long) ptr);
                                il.Emit(System.Reflection.Emit.OpCodes.Conv_I);
                                instr.OpCode = Mono.Cecil.Cil.OpCodes.Calli;
                                operand = ((MethodReference) instr.Operand).ResolveReflectionSignature(mb.Module);
                            }
                        } else {
                            throw new NotSupportedException($"Unsupported global method operand on opcode {instr.OpCode.Name}");
                        }
                    }
#endif

                    if (operand == null)
                        throw new NullReferenceException($"Unexpected null in {def} @ {instr}");

                    Type operandType = operand.GetType();
                    if (!_Emitters.TryGetValue(operandType, out MethodInfo emit))
                        emit = _Emitters.FirstOrDefault(kvp => kvp.Key.IsAssignableFrom(operandType)).Value;
                    if (emit == null)
                        throw new InvalidOperationException($"Unexpected unemittable {operand.GetType().FullName} in {def} @ {instr}");

                    emitArgs[0] = _ReflOpCodes[instr.OpCode.Value];
                    emitArgs[1] = operand;
                    emit.Invoke(il, emitArgs);
                }

                // TODO: This can be improved perf-wise!
                foreach (ExceptionHandler handler in def.Body.ExceptionHandlers) {
                    if (handler.HandlerEnd == instr.Next) {
                        il.EndExceptionBlock();
                    }
                }

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
        }

    }
}
