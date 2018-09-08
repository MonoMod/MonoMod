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
    public sealed class DynamicMethodDefinition {

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

        public readonly MethodDefinition Definition;

        public readonly Module Context;
        public readonly MethodBase Original;

        public readonly DynamicMethod Dynamic;

        public DynamicMethodDefinition(MethodDefinition def, MethodBase original = null) {
            Definition = def;

            Original = original ?? (Assembly.Load(def.Module.Assembly.Name.FullName).GetModule(def.Module.Name)).ResolveMethod(def.MetadataToken.ToInt32());
            Type[] genericArgsType = Original.DeclaringType.IsGenericType ? Original.DeclaringType.GetGenericArguments() : null;
            Type[] genericArgsMethod = Original.IsGenericMethod ? Original.GetGenericArguments() : null;

            ParameterInfo[] args = Original.GetParameters();
            Type[] argTypes;
            if (!Original.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = Original.DeclaringType;
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            Dynamic = new DynamicMethod(
                "DynamicMethodDefinition:" + def.DeclaringType.FullName + "::" + def.Name,
                (Original as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                Original.DeclaringType,
                true // If any random errors pop up, try setting this to false first.
            );
            ILGenerator il = Dynamic.GetILGenerator();

            LocalBuilder[] locals = Definition.Body.Variables.Select(
                var => il.DeclareLocal(ResolveMember(var.VariableType, genericArgsType, genericArgsMethod) as Type, var.IsPinned)
            ).ToArray();

            // Pre-pass - Set up label map.
            Dictionary<int, Label> labelMap = new Dictionary<int, Label>();
            foreach (Instruction instr in Definition.Body.Instructions) {
                if (instr.Operand is Instruction[] targets) {
                    foreach (Instruction target in targets) {
                        if (!labelMap.ContainsKey(target.Offset)) {
                            labelMap[target.Offset] = il.DefineLabel();
                        }
                    }

                } else if (instr.Operand is Instruction target) {
                    if (!labelMap.ContainsKey(target.Offset)) {
                        labelMap[target.Offset] = il.DefineLabel();
                    }
                }
            }

            object[] emitArgs = new object[2];
            foreach (Instruction instr in Definition.Body.Instructions) {
                if (labelMap.TryGetValue(instr.Offset, out Label label)) {
                    il.MarkLabel(label);
                }

                // TODO: Handle special blocks!

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

                if (instr.OpCode.OperandType == Mono.Cecil.Cil.OperandType.InlineNone)
                    il.Emit(_ReflOpCodes[instr.OpCode.Value]);
                else {
                    if (operand == null)
                        throw new NullReferenceException($"Unexpected null @ {Definition} @ {instr}");

                    Type operandType = operand.GetType();
                    if (!_Emitters.TryGetValue(operandType, out MethodInfo emit)) {
                        emit = _Emitters.FirstOrDefault(kvp => kvp.Key.IsAssignableFrom(operandType)).Value;
                    }
                    if (emit == null)
                        throw new InvalidOperationException($"Unexpected unemittable {operand.GetType().FullName} @ {Definition} @ {instr}");

                    emitArgs[0] = _ReflOpCodes[instr.OpCode.Value];
                    emitArgs[1] = operand;
                    emit.Invoke(il, emitArgs);
                }

                // TODO: Handle special blocks!

            }

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

        public static implicit operator MethodDefinition(DynamicMethodDefinition v) => v.Definition;
        public static implicit operator DynamicMethod(DynamicMethodDefinition v) => v.Dynamic;

    }
}
