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
using System.Diagnostics.SymbolStore;
#endif

namespace MonoMod.Utils {
    public sealed partial class DynamicMethodDefinition : IDisposable {

        private static readonly Dictionary<short, System.Reflection.Emit.OpCode> _ReflOpCodes = new Dictionary<short, System.Reflection.Emit.OpCode>();
        private static readonly Dictionary<short, Mono.Cecil.Cil.OpCode> _CecilOpCodes = new Dictionary<short, Mono.Cecil.Cil.OpCode>();
        private static readonly Dictionary<Type, MethodInfo> _Emitters = new Dictionary<Type, MethodInfo>();

        private static readonly ConstructorInfo c_DebuggableAttribute = typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });

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
        public MethodDefinition Definition =>
            (_Module.LookupToken(Method.GetMetadataToken()) as MethodReference)?.Resolve() ??
            throw new InvalidOperationException("Method definition not found");

#if !NETSTANDARD
        public TypeBuilder TypeBuilder;
#endif

        public DynamicMethodDefinition(MethodBase method, Func<AssemblyName, ModuleDefinition> moduleGen = null) {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Reload(moduleGen, false);
        }

        public void Reload(Func<AssemblyName, ModuleDefinition> moduleGen = null, bool force = false) {
            ModuleDefinition moduleTmp = null;

            if (moduleGen != null)
                _ModuleGen = moduleGen;

            try {
                ModuleDefinition module = (moduleGen ?? _ModuleGen)?.Invoke(Method.Module.Assembly.GetName());
                if (module == null) {
                    if (_Module != null && !force) {
                        module = _Module;
                    } else {
#if !CECIL0_9
                        _Module?.Dispose();
#endif
                        _Module = null;
                        ReaderParameters rp = new ReaderParameters();
                        if (_ModuleGen != null)
                            rp.AssemblyResolver = new AssemblyCecilDefinitionResolver(_ModuleGen, rp.AssemblyResolver ?? new DefaultAssemblyResolver());
                        module = moduleTmp = ModuleDefinition.ReadModule(Method.DeclaringType.GetTypeInfo().Assembly.GetLocation(), rp);
                    }
                }
                _Module = module;
                _ModuleRef++;
            } catch when (_DisposeEarly()) {
            }

            bool _DisposeEarly() {
                if (moduleTmp != null) {
#if !CECIL0_9
                    moduleTmp.Dispose();
#endif
                    _Module = null;
                    _ModuleRef = 0;
                }
                return false;
            }
        }

        public MethodInfo GenerateAuto(object typeBuilder = null) {
#if NETSTANDARD
            return Generate();
#else
            return Generate(typeBuilder as TypeBuilder);
#endif
        }

        public DynamicMethod Generate() {
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
                $"DynamicMethodDefinition<{Method}>",
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                Method.DeclaringType,
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = dm.GetILGenerator();

            _Generate(dm, il);

            return dm;
        }

#if !NETSTANDARD
        public MethodInfo Generate(TypeBuilder typeBuilder) {
            MethodBuilder method = GenerateMethodBuilder(typeBuilder);
            typeBuilder = (TypeBuilder) method.DeclaringType;
            Type type = typeBuilder.CreateType();
            return type.GetMethod(method.Name);
        }

        public MethodBuilder GenerateMethodBuilder(TypeBuilder typeBuilder) {
            if (typeBuilder == null)
                typeBuilder = TypeBuilder;
            if (typeBuilder == null) {
                AssemblyBuilder ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = $"DynamicMethodDefinitionAssembly.{Method}.{GetHashCode()}"
                    },
                    AssemblyBuilderAccess.RunAndSave
                );

                ab.SetCustomAttribute(new CustomAttributeBuilder(c_DebuggableAttribute, new object[] {
                    DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.Default
                }));

                ModuleBuilder module = ab.DefineDynamicModule($"DynamicMethodDefinitionAssembly<{Method}>?{GetHashCode()}.dll", true);
                typeBuilder = TypeBuilder = module.DefineType("MainType", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
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
                Method.Name,
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
#endif

            MethodDefinition def = Definition;
            MethodDebugInformation defInfo = def.DebugInformation;
            // Fix up any mistakes which might accidentally pop up.
            def.ConvertShortLongOps();

            LocalBuilder[] locals = def.Body.Variables.Select(
                var => {
                    LocalBuilder local = il.DeclareLocal(var.VariableType.ResolveReflection(), var.IsPinned);
#if !NETSTANDARD
                    if (mb != null && defInfo != null && defInfo.TryGetName(var, out string name)) {
                        local.SetLocalSymInfo(name);
                    }
#endif
                    return local;
                }
            ).ToArray();

            // Pre-pass - Set up label map.
            Dictionary<int, Label> labelMap = new Dictionary<int, Label>();
            foreach (Instruction instr in def.Body.Instructions) {
                if (instr.Operand is Instruction[] targets) {
                    foreach (Instruction target in targets)
                        if (!labelMap.ContainsKey(target.Offset))
                            labelMap[target.Offset] = il.DefineLabel();

                } else if (instr.Operand is Instruction target) {
                    if (!labelMap.ContainsKey(target.Offset))
                        labelMap[target.Offset] = il.DefineLabel();
                }
            }

#if !NETSTANDARD
            Dictionary<Document, ISymbolDocumentWriter> infoDocCache = mb == null ? null : new Dictionary<Document, ISymbolDocumentWriter>();
#endif

            object[] emitArgs = new object[2];
            foreach (Instruction instr in def.Body.Instructions) {
                if (labelMap.TryGetValue(instr.Offset, out Label label))
                    il.MarkLabel(label);

#if !NETSTANDARD
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
                        operand = targets.Select(target => labelMap[target.Offset]).ToArray();
                    } else if (operand is Instruction target) {
                        operand = labelMap[target.Offset];
                    } else if (operand is VariableDefinition var) {
                        operand = locals[var.Index];
                    } else if (operand is ParameterDefinition param) {
                        operand = param.Index;
                    } else if (operand is MemberReference mref) {
                        operand = mref.ResolveReflection();
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
            if (_Module != null && (--_ModuleRef) == 0) {
#if !CECIL0_9
                _Module.Dispose();
#endif
                _Module = null;
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

    }
}
