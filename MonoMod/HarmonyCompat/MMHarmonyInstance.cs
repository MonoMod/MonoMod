using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using StringInject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonoMod.NET40Shim;
using MonoMod.Helpers;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonoMod.HarmonyCompat {
    public class MMHarmonyInstance {

        public MonoModder Modder;
        public object Harmony;

        public Assembly Target;
        public Module TargetModule;

        public MMHarmonyInstance(MonoModder modder, Assembly target) {
            HarmonyHelper.Init();
            Modder = modder;
            Harmony = HarmonyHelper.CreateInstance($"monomod.{modder.Module.Assembly.Name.Name}.{MMILProxyManager.GetId(modder)}");
            Target = target;
            TargetModule = Target.GetModule(modder.Module.Name);
        }

        public void Log(string str)
            => Modder?.Log("[Harmony] " + str);

        public virtual void PatchAll() {
            foreach (TypeDefinition type in Modder.Module.Types)
                PatchType(type);
        }

        public virtual void PatchType(TypeDefinition type) {
            string typeName = type.FullName;

            // Harmony has its own set of problems with those.
            if (type.HasGenericParameters) {
                // Log($"[PatchMethod] Not patching type {typeName} in target assembly {Target.FullName} - generics limited by Harmony");
                return;
            }

            Type targetType = Target.GetType(typeName.Replace('/', '+'));
            if (targetType == null) {
                // What to do, what to do...
                // Log($"[PatchType] Type {typeName} not found in target assembly {Target.FullName}");
                return;
            }

            // TODO: [MMHarmony] Dynamically add new fields, methods and properties.
            foreach (MethodDefinition method in type.Methods)
                PatchMethod(targetType, method);
            
            foreach (TypeDefinition nested in type.NestedTypes)
                PatchType(nested);
        }

        public virtual void PatchMethod(Type targetType, MethodDefinition method) {
            if (!method.HasBody)
                return;

            string methodName = method.Name;
            // Harmony has its own set of problems with those.
            if (method.HasGenericParameters) {
                // Log($"[PatchMethod] Not patching method {method.FullName} in target assembly {Target.FullName} - generics limited by Harmony");
                return;
            }


            MethodBase targetMethod = FindMethod(targetType, method);
            if (targetMethod == null) {
                // What to do, what to do...
                // Log($"[PatchMethod] Method {methodName} not found in target type {targetType.FullName} in {Target.FullName}");
                return;
            }

            // TODO: [Harmony] Parse original code and check if patch needed.
            Mono.Cecil.Cil.MethodBody body = method.Body;
            System.Reflection.MethodBody targetBody = targetMethod.GetMethodBody();
            if (body.Variables.Count == targetBody.LocalVariables.Count) {
                Collection<Instruction> instrs = body.Instructions;
                byte[] targetCode = targetMethod.GetMethodBody().GetILAsByteArray();
                bool match = true;
                int i = 0;
                using (MemoryStream ms = new MemoryStream(targetCode))
                using (BinaryReader targetCodeReader = new BinaryReader(ms)) {
                    for (; ms.Position < targetCode.Length && i < instrs.Count; i++) {
                        Instruction instr = instrs[i];
                        // FIXME: [Harmony] This seems to be wrong..?
                        /*
                        if (instr.Offset != ms.Position) {
                            Console.WriteLine($"Mismatch @ {i} ({ms.Position}): Position: {ms.Position} v {instr.Offset}");
                            match = false;
                            break;
                        }
                        */
                        /*
                        OpCode targetOpCode = _ReadOpCode(targetCodeReader);
                        if (targetOpCode != instr.OpCode) {
                            Console.WriteLine($"Mismatch @ {i} ({ms.Position}): OpCode: {targetOpCode} v {instr.OpCode}");
                            match = false;
                            break;
                        }
                        object targetOperand = _ReadOperand(targetCodeReader, targetOpCode);
                        object instrOperand = instr.Operand;
                        if (instrOperand is IMetadataTokenProvider)
                            instrOperand = ((IMetadataTokenProvider) instrOperand).MetadataToken.ToInt32();
                        if (targetOperand != null && targetOperand != instrOperand) {
                            // FIXME: [Harmony]  Inlined token RIDs wrong - always the same on Cecil - side?!
                            // Console.WriteLine($"Mismatch @ {i} ({ms.Position}): OpCode: {targetOpCode}; OperandType: {targetOpCode.OperandType}; Operand: {targetOperand} ({targetOperand?.GetType()}) v {instrOperand} ({instrOperand?.GetType()})");
                            // match = false;
                            // break;
                        }
                        */
                    }
                    if (match && true)
                        // ms.Position >= targetCode.Length &&
                        // i >= instrs.Count)
                        return;
                }
            }

            Log($"[PatchMethod] Creating transpiler for {method.FullName} in target assembly {Target.FullName}");
            MMHarmonyTranspiler transpiler = new MMHarmonyTranspiler(this, method, targetMethod);
            object transpilerProxy = HarmonyHelper.NewTranspilerProxy(transpiler);
            MethodInfo transpilerGetter = HarmonyHelper.NewTranspilerGetter(transpilerProxy);
            object transpilerHarmonyMethod = HarmonyHelper.NewHarmonyMethod(transpilerGetter);
            HarmonyHelper.PatchTranspiler(Harmony, targetMethod, transpilerHarmonyMethod);
        }

        
        public static MethodBase FindMethod(Type declaring, MethodReference method) {
            // Fails on <T>(T something)
            /*
            return declaring.GetMethod(
                method.Name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                method.Parameters.Select(p => FindType(p.ParameterType, method)).ToArray(),
                null
            );
            */

            // Skips T Graph::Clone(T)
            MethodInfo[] methods = declaring.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int mi = 0; mi < methods.Length; mi++) {
                MethodInfo methodRefl = methods[mi];
                ParameterInfo[] args = methodRefl.GetParameters();
                Type[] genArgs = methodRefl.GetGenericArguments();
                if (methodRefl.Name != method.Name ||
                    args.Length != method.Parameters.Count ||
                    genArgs.Length != method.GenericParameters.Count ||
                    methodRefl.ReturnType.FullName != method.ReturnType.FullName.Replace('/', '+'))
                    continue;

                bool match = true;

                for (int pi = 0; pi < args.Length; pi++) {
                    ParameterDefinition arg = method.Parameters[pi];
                    ParameterInfo argRefl = args[pi];
                    if (arg.ParameterType.FullName.Replace('/', '+') != argRefl.ParameterType.FullName ||
                        arg.Name != argRefl.Name ||
                        arg.IsIn != argRefl.IsIn ||
                        arg.IsLcid != argRefl.IsLcid ||
                        arg.IsOptional != argRefl.IsOptional ||
                        arg.IsOut != argRefl.IsOut ||
                        arg.IsReturnValue != argRefl.IsRetval) {
                        match = false;
                        break;
                    }
                }
                if (!match)
                    continue;

                for (int pi = 0; pi < genArgs.Length; pi++) {
                    GenericParameter arg = method.GenericParameters[pi];
                    Type argRefl = genArgs[pi];
                    if (arg.Name != argRefl.Name) {
                        match = false;
                        break;
                    }
                }
                if (!match)
                    continue;

                return methodRefl;
            }

            return null;
        }

        private readonly static Dictionary<string, Type> _TypeCache = new Dictionary<string, Type>();
        public static Type FindType(TypeReference type, IGenericParameterProvider context) {
            if (type is TypeSpecification) {
                TypeSpecification ts = (TypeSpecification) type;
                Type elem = FindType(ts.ElementType, context);

                if (type.IsByReference)
                    return elem.MakeByRefType();

                if (type.IsPointer)
                    return elem.MakePointerType();

                if (type.IsPinned)
                    throw new InvalidOperationException("MonoMod.HarmonyCompat can't handle pinned types!");

                if (type.IsArray)
                    return elem.MakeArrayType(((ArrayType) type).Dimensions.Count);

                if (type.IsRequiredModifier)
                    throw new InvalidOperationException("MonoMod.HarmonyCompat can't handle required modifier types!");

                if (type.IsOptionalModifier)
                    throw new InvalidOperationException("MonoMod.HarmonyCompat can't handle optional modifier types!");

                if (type.IsGenericInstance)
                    return elem.MakeGenericType(
                        ((GenericInstanceType) type).GenericArguments.Select(a => FindType(a, context)).ToArray()
                    );

                if (type.IsFunctionPointer)
                    throw new InvalidOperationException("MonoMod.HarmonyCompat can't handle function pointer types!");

                throw new InvalidOperationException($"MonoMod.HarmonyCompat can't handle TypeSpecification: {type.FullName} ({type.GetType()})");
            }

            if (type.IsGenericParameter) {
                GenericParameter genParam = context.GetGenericParameter(((GenericParameter) type).Name);

                if (genParam.Owner is MethodReference)
                    return
                        FindMethod(
                            FindType(((MethodReference) genParam.Owner).DeclaringType, context),
                            (MethodReference) genParam.Owner
                        ).GetGenericArguments()[genParam.Position];
                else if (genParam.Owner is TypeReference)
                    return
                        FindType((TypeReference) genParam.Owner, context)
                        .GetGenericArguments()[genParam.Position];

                throw new InvalidOperationException($"MonoMod.HarmonyCompat can't handle generic parameter owner: {genParam.Owner} ({genParam.Owner.GetType()})");
            }

            return FindType(type.FullName.Replace('/', '+'));
        }
        public static Type FindType(string fullname, string ns = null, string name = null) {
            Type type;
            if (_TypeCache.TryGetValue(fullname, out type))
                return type;

            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
                if ((type = asms[i].GetType(fullname, false)) != null)
                    return _TypeCache[fullname] = type;

            if (type == null && ns != null && name != null) {
                for (int i = 0; i < asms.Length; i++) {
                    Assembly asm = asms[i];
                    Type[] types = asm.GetTypes();
                    for (int ti = 0; ti < types.Length; ti++) {
                        type = types[ti];
                        if (type.Namespace == ns && type.Name == name) {
                            return _TypeCache[fullname] = type;
                        }
                    }
                }
            }

            return _TypeCache[fullname] = null;
        }


        private OpCode _ReadOpCode(BinaryReader reader) {
            byte op = reader.ReadByte();
            return op != 0xfe
                ? _OpCodes[op]
                : _OpCodesLong[reader.ReadByte()];
        }

        private object _ReadOperand(BinaryReader reader, OpCode op) {
            int val;
            switch (op.OperandType) {
                case OperandType.InlineNone:
                    return null;

                case OperandType.InlineSwitch:
                    int length = reader.ReadInt32();
                    int baseOffset = ((int) reader.BaseStream.Position) + (4 * length);
                    int[] branches = new int[length];
                    for (int i = 0; i < length; i++)
                        branches[i] = reader.ReadInt32() + baseOffset;
                    return branches;

                case OperandType.ShortInlineBrTarget:
                    val = (sbyte) reader.ReadByte();
                    return val + ((int) reader.BaseStream.Position);

                case OperandType.InlineBrTarget:
                    val = reader.ReadInt32();
                    return val + ((int) reader.BaseStream.Position);

                case OperandType.ShortInlineI:
                    if (op == OpCodes.Ldc_I4_S)
                        return (sbyte) reader.ReadByte();
                    else
                        return reader.ReadByte();

                case OperandType.InlineI:
                    return reader.ReadInt32();

                case OperandType.ShortInlineR:
                    return reader.ReadSingle();

                case OperandType.InlineR:
                    return reader.ReadDouble();

                case OperandType.InlineI8:
                    return reader.ReadInt64();

                case OperandType.InlineSig:
                    return reader.ReadInt32();

                case OperandType.InlineString:
                    return TargetModule.ResolveString(reader.ReadInt32());

                case OperandType.InlineTok:
                    return reader.ReadInt32();

                case OperandType.InlineType:
                    return reader.ReadInt32();

                case OperandType.InlineMethod:
                    return reader.ReadInt32();

                case OperandType.InlineField:
                    return reader.ReadInt32();

                case OperandType.ShortInlineVar:
                    return reader.ReadByte();

                case OperandType.InlineVar:
                    return reader.ReadInt16();

                default:
                    return null;
            }
        }

        private readonly static OpCode[] _OpCodes;
        private readonly static OpCode[] _OpCodesLong;

        static MMHarmonyInstance() {
            _OpCodes = new OpCode[0xe1];
            _OpCodesLong = new OpCode[0x1f];
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (FieldInfo field in fields) {
                OpCode opcode = (OpCode) field.GetValue(null);
                if (opcode.OpCodeType == OpCodeType.Nternal)
                    continue;
                if (opcode.Size == 1)
                    _OpCodes[opcode.Value] = opcode;
                else
                    _OpCodesLong[opcode.Value & 0xff] = opcode;
            }
        }

    }
}
