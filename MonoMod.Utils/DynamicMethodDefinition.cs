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

        private readonly static Dictionary<short, System.Reflection.Emit.OpCode> _ReflOpCodes = new Dictionary<short, System.Reflection.Emit.OpCode>();
        private readonly static Dictionary<short, Mono.Cecil.Cil.OpCode> _CecilOpCodes = new Dictionary<short, Mono.Cecil.Cil.OpCode>();
        private readonly static Dictionary<Type, MethodInfo> _Emitters = new Dictionary<Type, MethodInfo>();

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

        private ModuleDefinition _Module;

        public readonly MethodBase Method;
        public readonly MethodDefinition Definition;

        public DynamicMethodDefinition(MethodBase method, ModuleDefinition module = null) {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            try {
                if (module == null)
                    _Module = module = ModuleDefinition.ReadModule(method.DeclaringType.Assembly.Location);
                Definition = (module.LookupToken(method.MetadataToken) as MethodReference)?.Resolve() ?? throw new ArgumentException("Method not found");
            } catch {
                _Module?.Dispose();
                throw;
            }
        }

        public DynamicMethodDefinition(MethodDefinition definition, MethodBase method = null) {
            Definition = definition;
            Method = method ?? (Assembly.Load(definition.Module.Assembly.Name.FullName).GetModule(definition.Module.Name)).ResolveMethod(definition.MetadataToken.ToInt32());
        }

        public DynamicMethod Generate() {
            Type[] genericArgsType = Method.DeclaringType.IsGenericType ? Method.DeclaringType.GetGenericArguments() : null;
            Type[] genericArgsMethod = Method.IsGenericMethod ? Method.GetGenericArguments() : null;

            ParameterInfo[] args = Method.GetParameters();
            Type[] argTypes;
            if (!Method.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = Method.DeclaringType;
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            DynamicMethod dynamic = new DynamicMethod(
                "DynamicMethodDefinition:" + Definition.DeclaringType.FullName + "::" + Definition.Name,
                (Method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                Method.DeclaringType,
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = dynamic.GetILGenerator();

            LocalBuilder[] locals = Definition.Body.Variables.Select(
                var => il.DeclareLocal(ResolveMember(var.VariableType, genericArgsType, genericArgsMethod) as Type, var.IsPinned)
            ).ToArray();

            // Pre-pass - Set up label map.
            Dictionary<int, Label> labelMap = new Dictionary<int, Label>();
            foreach (Instruction instr in Definition.Body.Instructions) {
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
            foreach (Instruction instr in Definition.Body.Instructions) {
                if (labelMap.TryGetValue(instr.Offset, out Label label))
                    il.MarkLabel(label);

                // TODO: This can be improved perf-wise!
                foreach (ExceptionHandler handler in Definition.Body.ExceptionHandlers) {
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
                                il.BeginCatchBlock(ResolveMember(handler.CatchType, genericArgsType, genericArgsMethod) as Type);
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
                    } else if (operand is MemberReference mref) {
                        operand = ResolveMember(mref, genericArgsType, genericArgsMethod);
                    }

                    if (operand == null)
                        throw new NullReferenceException($"Unexpected null @ {Definition} @ {instr}");

                    Type operandType = operand.GetType();
                    MethodInfo emit;
                    if (!_Emitters.TryGetValue(operandType, out emit))
                        emit = _Emitters.FirstOrDefault(kvp => kvp.Key.IsAssignableFrom(operandType)).Value;
                    if (emit == null)
                        throw new InvalidOperationException($"Unexpected unemittable {operand.GetType().FullName} @ {Definition} @ {instr}");

                    emitArgs[0] = _ReflOpCodes[instr.OpCode.Value];
                    emitArgs[1] = operand;
                    emit.Invoke(il, emitArgs);
                }

                // TODO: This can be improved perf-wise!
                foreach (ExceptionHandler handler in Definition.Body.ExceptionHandlers) {
                    if (handler.HandlerEnd == instr.Next) {
                        il.EndExceptionBlock();
                    }
                }

            }

            return dynamic;
        }

        private static MemberInfo ResolveMember(MemberReference mref, Type[] genericTypeArguments, Type[] genericMethodArguments) {
            MemberReference mdef = mref.Resolve() as MemberReference;

            MemberInfo info = Assembly.Load(mdef.Module.Assembly.Name.FullName)
                .GetModule(mdef.Module.Name)
                .ResolveMember(mdef.MetadataToken.ToInt32(), genericTypeArguments, genericMethodArguments);

            if (mref is TypeSpecification ts) {
                Type type = ResolveMember(ts.ElementType, genericTypeArguments, genericMethodArguments) as Type;

                if (ts.IsByReference)
                    return type.MakeByRefType();

                if (ts.IsPointer)
                    return type.MakePointerType();

                if (ts.IsArray)
                    return type.MakeArrayType((ts as ArrayType).Dimensions.Count);

                if (ts.IsGenericInstance)
                    return type.MakeGenericType((ts as GenericInstanceType).GenericArguments.Select(arg => ResolveMember(arg, genericTypeArguments, genericMethodArguments) as Type).ToArray());

            } else if (mref is GenericInstanceMethod mrefGenMethod) {
                return (info as MethodInfo).MakeGenericMethod(mrefGenMethod.GenericArguments.Select(arg => ResolveMember(arg, genericTypeArguments, genericMethodArguments) as Type).ToArray());
            }

            return info;
        }

        public void Dispose() {
            _Module?.Dispose();
            _Module = null;
        }

    }
}
