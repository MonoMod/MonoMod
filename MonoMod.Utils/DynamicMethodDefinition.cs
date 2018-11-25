using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace MonoMod.Utils {
    public sealed class DynamicMethodDefinition : IDisposable {

        /* Note:
         * The following doesn't work with ancient versions of Mono before
         * referencesource was used. If you really want to support those,
         * even though they don't even support mixed mode assemblies anyway,
         * ... good luck.
         */

        // ModuleBuilder has only got a single constructor in the reference source.
        private static ConstructorInfo c_ModuleBuilder =
            typeof(DynamicMethod).GetTypeInfo().Assembly
            .GetType("System.Reflection.Emit.ModuleBuilder").GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        private static readonly FastReflectionDelegate dmd_DynamicMethod_GetDynamicMethodsModule =
            typeof(DynamicMethod).GetMethod("GetDynamicMethodsModule", BindingFlags.NonPublic | BindingFlags.Static)
            ?.CreateFastDelegate();
#if NETSTANDARD
        private static readonly FastReflectionDelegate dmd_AssemblyBuilder_DefineDynamicAssembly =
            typeof(DynamicMethod).GetTypeInfo().Assembly
            .GetType("System.Reflection.Emit.AssemblyBuilder").GetMethods().FirstOrDefault(
                m => m.GetFindableID(withType: false) == "System.Reflection.Emit.AssemblyBuilder DefineDynamicAssembly(System.Reflection.AssemblyName,System.Reflection.Emit.AssemblyBuilderAccess)"
            )
            ?.CreateFastDelegate();
#else
        private static AssemblyBuilder dmd_AssemblyBuilder_DefineDynamicAssembly(object instance, AssemblyName name, int access)
            => AppDomain.CurrentDomain.DefineDynamicAssembly(name, (AssemblyBuilderAccess) access);
#endif
        private static readonly Module _DynamicMethodModuleBuilder =
            c_ModuleBuilder.Invoke(new object[] {
                dmd_AssemblyBuilder_DefineDynamicAssembly(null, new AssemblyName("DynamicMethodDefinitionHelper"), 1 /* Run */),
                dmd_DynamicMethod_GetDynamicMethodsModule(null)
            }) as Module;

        private static readonly Dictionary<short, System.Reflection.Emit.OpCode> _ReflOpCodes = new Dictionary<short, System.Reflection.Emit.OpCode>();
        private static readonly Dictionary<short, Mono.Cecil.Cil.OpCode> _CecilOpCodes = new Dictionary<short, Mono.Cecil.Cil.OpCode>();
        private static readonly Dictionary<Type, MethodInfo> _Emitters = new Dictionary<Type, MethodInfo>();

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

        public DynamicMethod Generate() {
            MethodDefinition def = Definition;

            Type[] genericArgsType = Method.DeclaringType.GetTypeInfo().IsGenericType ? Method.DeclaringType.GetGenericArguments() : null;
            Type[] genericArgsMethod = Method.IsGenericMethod ? Method.GetGenericArguments() : null;

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
                "DynamicMethodDefinition:" + def.DeclaringType.FullName + "::" + def.Name,
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                Method.DeclaringType,
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = dm.GetILGenerator();

            LocalBuilder[] locals = def.Body.Variables.Select(
                var => il.DeclareLocal(var.VariableType.ResolveReflection(), var.IsPinned)
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

            object[] emitArgs = new object[2];
            foreach (Instruction instr in def.Body.Instructions) {
                if (labelMap.TryGetValue(instr.Offset, out Label label))
                    il.MarkLabel(label);

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
                        operand = csite.ResolveReflection(_DynamicMethodModuleBuilder);
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

            return dm;
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
