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
using System.Security.Permissions;
using System.Security;
using System.Diagnostics.SymbolStore;

namespace MonoMod.Utils {
    public sealed partial class DynamicMethodDefinition {

        private static readonly Dictionary<short, System.Reflection.Emit.OpCode> _ReflOpCodes = new Dictionary<short, System.Reflection.Emit.OpCode>();
        private static readonly Dictionary<short, Mono.Cecil.Cil.OpCode> _CecilOpCodes = new Dictionary<short, Mono.Cecil.Cil.OpCode>();
        private static readonly Dictionary<Type, MethodInfo> _Emitters = new Dictionary<Type, MethodInfo>();

#if !NETSTANDARD
        private static readonly bool _MBCanRunAndCollect = Enum.IsDefined(typeof(AssemblyBuilderAccess), "RunAndCollect");
#endif

        static void _InitReflEmit() {
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

        public DynamicMethod GenerateViaDynamicMethod() {
            Type[] argTypes;

            if (Method != null) {
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

            } else {
                int offs = 0;
                if (Definition.HasThis) {
                    offs++;
                    argTypes = new Type[Definition.Parameters.Count + 1];
                    Type type = Definition.DeclaringType.ResolveReflection();
                    if (type.IsValueType)
                        type = type.MakeByRefType();
                    argTypes[0] = type;
                } else {
                    argTypes = new Type[Definition.Parameters.Count];
                }
                for (int i = 0; i < Definition.Parameters.Count; i++)
                    argTypes[i + offs] = Definition.Parameters[i].ParameterType.ResolveReflection();
            }

            DynamicMethod dm = new DynamicMethod(
                $"DMD<{Method?.GetFindableID(simple: true) ?? Definition.GetFindableID(simple: true)}>",
                (Method as MethodInfo)?.ReturnType ?? Definition.ReturnType?.ResolveReflection() ?? typeof(void), argTypes,
                Method?.DeclaringType ?? typeof(DynamicMethodDefinition),
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = dm.GetILGenerator();

            for (int i = 0; i < 10; i++) {
                // Prevent mono from inlining the DynamicMethod.
                il.Emit(System.Reflection.Emit.OpCodes.Nop);
            }

            _GenerateEmit(dm, il);

            return (DynamicMethod) _Postbuild(dm);
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
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                ((AssemblyBuilder) typeBuilder.Assembly).Save(name);
            }
            return _Postbuild(
                type.GetMethod(method.Name, BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            );
        }

        public MethodBuilder GenerateMethodBuilder(TypeBuilder typeBuilder) {
            if (typeBuilder == null) {
                string dumpDir = Environment.GetEnvironmentVariable("MONOMOD_DMD_DUMP");
                if (string.IsNullOrEmpty(dumpDir)) {
                    dumpDir = null;
                } else {
                    dumpDir = System.IO.Path.GetFullPath(dumpDir);
                }
                bool collect = string.IsNullOrEmpty(dumpDir) && _MBCanRunAndCollect;
                AssemblyBuilder ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName() {
                        Name = GetDumpName("MethodBuilder")
                    },
                    collect ? (AssemblyBuilderAccess) 9 : AssemblyBuilderAccess.RunAndSave,
                    dumpDir
                );

                ab.SetCustomAttribute(new CustomAttributeBuilder(c_UnverifiableCodeAttribute, new object[] {
                }));

                if (Debug) {
                    ab.SetCustomAttribute(new CustomAttributeBuilder(c_DebuggableAttribute, new object[] {
                        DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.Default
                    }));
                }

                // Note: Debugging can fail on mono if Mono.CompilerServices.SymbolWriter.dll cannot be found,
                // or if Mono.CompilerServices.SymbolWriter.SymbolWriterImpl can't be found inside of that.
                // https://github.com/mono/mono/blob/f879e35e3ed7496d819bd766deb8be6992d068ed/mcs/class/corlib/System.Reflection.Emit/ModuleBuilder.cs#L146
                ModuleBuilder module = ab.DefineDynamicModule($"{ab.GetName().Name}.dll", $"{ab.GetName().Name}.dll", Debug);
                typeBuilder = module.DefineType(
                    $"DMD<{Method?.GetFindableID(simple: true)?.Replace('.', '_')}>?{GetHashCode()}",
                    System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Class
                );
            }

            Type[] argTypes;
            Type[][] argTypesModReq;
            Type[][] argTypesModOpt;

            if (Method != null) {
                ParameterInfo[] args = Method.GetParameters();
                int offs = 0;
                if (!Method.IsStatic) {
                    offs++;
                    argTypes = new Type[args.Length + 1];
                    argTypesModReq = new Type[args.Length + 1][];
                    argTypesModOpt = new Type[args.Length + 1][];
                    argTypes[0] = Method.GetThisParamType();
                    argTypesModReq[0] = Type.EmptyTypes;
                    argTypesModOpt[0] = Type.EmptyTypes;
                } else {
                    argTypes = new Type[args.Length];
                    argTypesModReq = new Type[args.Length][];
                    argTypesModOpt = new Type[args.Length][];
                }

                for (int i = 0; i < args.Length; i++) {
                    argTypes[i + offs] = args[i].ParameterType;
                    argTypesModReq[i + offs] = args[i].GetRequiredCustomModifiers();
                    argTypesModOpt[i + offs] = args[i].GetOptionalCustomModifiers();
                }

            } else {
                int offs = 0;
                if (Definition.HasThis) {
                    offs++;
                    argTypes = new Type[Definition.Parameters.Count + 1];
                    argTypesModReq = new Type[Definition.Parameters.Count + 1][];
                    argTypesModOpt = new Type[Definition.Parameters.Count + 1][];
                    Type type = Definition.DeclaringType.ResolveReflection();
                    if (type.IsValueType)
                        type = type.MakeByRefType();
                    argTypes[0] = type;
                    argTypesModReq[0] = Type.EmptyTypes;
                    argTypesModOpt[0] = Type.EmptyTypes;
                } else {
                    argTypes = new Type[Definition.Parameters.Count];
                    argTypesModReq = new Type[Definition.Parameters.Count][];
                    argTypesModOpt = new Type[Definition.Parameters.Count][];
                }

                List<Type> modReq = new List<Type>();
                List<Type> modOpt = new List<Type>();

                for (int i = 0; i < Definition.Parameters.Count; i++) {
                    _EmitResolveWithModifiers(Definition.Parameters[i].ParameterType, out Type paramType, out Type[] paramTypeModReq, out Type[] paramTypeModOpt, modReq, modOpt);
                    argTypes[i + offs] = paramType;
                    argTypesModReq[i + offs] = paramTypeModReq;
                    argTypesModOpt[i + offs] = paramTypeModOpt;
                }
            }

            // Required because the return type modifiers aren't easily accessible via reflection.
            _EmitResolveWithModifiers(Definition.ReturnType, out Type returnType, out Type[] returnTypeModReq, out Type[] returnTypeModOpt);

            MethodBuilder mb = typeBuilder.DefineMethod(
                (Method?.Name ?? Definition.Name).Replace('.', '_'),
                System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                CallingConventions.Standard,
                returnType, returnTypeModReq, returnTypeModOpt,
                argTypes, argTypesModReq, argTypesModOpt
            );
            ILGenerator il = mb.GetILGenerator();

            _GenerateEmit(mb, il);

            return mb;
        }
#endif

        private void _GenerateEmit(MethodBase _mb, ILGenerator il) {
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
            bool checkTryEndEarly = false;
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

                foreach (ExceptionHandler handler in def.Body.ExceptionHandlers) {
                    if (checkTryEndEarly && handler.HandlerEnd == instr) {
                        il.EndExceptionBlock();
                    }

                    if (handler.TryStart == instr) {
                        il.BeginExceptionBlock();
                    } else if (handler.FilterStart == instr) {
                        il.BeginExceptFilterBlock();
                    } else if (handler.HandlerStart == instr) {
                        switch (handler.HandlerType) {
                            case ExceptionHandlerType.Filter:
                                il.BeginCatchBlock(null);
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

                    // Avoid duplicate endfilter / endfinally
                    if (handler.HandlerStart == instr.Next) {
                        switch (handler.HandlerType) {
                            case ExceptionHandlerType.Filter:
                                if (instr.OpCode == Mono.Cecil.Cil.OpCodes.Endfilter)
                                    goto SkipEmit;
                                break;
                            case ExceptionHandlerType.Finally:
                                if (instr.OpCode == Mono.Cecil.Cil.OpCodes.Endfinally)
                                    goto SkipEmit;
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
#if !CECIL0_9
                        if (mref is DynamicMethodReference dmref) {
                            operand = dmref.DynamicMethod;

                        } else
#endif
                        {
                            MemberInfo member = mref.ResolveReflection();
                            operand = member;
#if !NETSTANDARD
                            // TODO: Only do the following for inaccessible members.
                            if (mb != null && member != null) {
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
                        }

                    } else if (operand is CallSite csite) {
                        if (dm != null) {
                            // SignatureHelper in unmanaged contexts cannot be fully made use of for DynamicMethods.
                            _EmitCallSite(dm, il, _ReflOpCodes[instr.OpCode.Value], csite);
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
                                operand = _EmitMethodProxy(mb, target);

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

                if (!checkTryEndEarly) {
                    foreach (ExceptionHandler handler in def.Body.ExceptionHandlers) {
                        if (handler.HandlerEnd == instr.Next) {
                            il.EndExceptionBlock();
                        }
                    }
                }

                checkTryEndEarly = false;
                continue;

                SkipEmit:
                checkTryEndEarly = true;
                continue;
            }
        }

        private static void _EmitResolveWithModifiers(TypeReference typeRef, out Type type, out Type[] typeModReq, out Type[] typeModOpt, List<Type> modReq = null, List<Type> modOpt = null) {
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

    }
}
