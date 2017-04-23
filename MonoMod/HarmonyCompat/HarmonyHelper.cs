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
using System.Runtime.CompilerServices;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using System.Reflection.Emit;
using System.Collections;

namespace MonoMod.HarmonyCompat {
    public static class HarmonyHelper {

        public static Assembly Harmony;

        public static Type t_HarmonyInstance;
        public static Type t_HarmonyMethod;
        public static Type t_CodeInstruction;

        public static Assembly HarmonyProxy;
        public static Type t_MMHarmonyTranspilerProxy;
        public static MethodInfo m_t_MMHarmonyTranspilerProxy_Init;

        public static ModuleBuilder HarmonyRuntime;

        private static DynamicMethodDelegate _CreateInstance;
        public static object CreateInstance(string name) =>
            _CreateInstance(null, name);

        private static DynamicMethodDelegate _PatchTranspiler;
        public static object PatchTranspiler(object self, MethodBase original, object transpiler) =>
            _PatchTranspiler(self, original, null, null, transpiler);

        private static DynamicMethodDelegate _NewHarmonyMethod;
        public static object NewHarmonyMethod(MethodInfo method) =>
            _NewHarmonyMethod(null, method);


        private static DynamicMethodDelegate _NewTranspilerProxy;
        public static object NewTranspilerProxy(MMHarmonyTranspiler transpiler) =>
            _NewTranspilerProxy(null, transpiler);

        public static MethodInfo NewTranspilerGetter(object transpiler) {
            string tn_getter = $"MMHarmonyTranspilerGetter_{transpiler.GetHashCode()}";
            Type t_getter = HarmonyRuntime.GetType(tn_getter);
            if (t_getter != null)
                return t_getter.GetMethod("GetTranspiler");

            TypeBuilder tb_getter = HarmonyRuntime.DefineType(
                tn_getter,
                System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Abstract
            );

            FieldBuilder fb_Instance = tb_getter.DefineField(
                "Instance",
                t_MMHarmonyTranspilerProxy, System.Reflection.FieldAttributes.Public | System.Reflection.FieldAttributes.Static
            );

            MethodBuilder mb_GetTranspiler = tb_getter.DefineMethod(
                "GetTranspiler",
                System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                t_MMHarmonyTranspilerProxy,
                new Type[] { typeof(ILGenerator), typeof(MethodBase), typeof(IEnumerable<>).MakeGenericType(t_CodeInstruction) }
            );
            mb_GetTranspiler.DefineParameter(0, System.Reflection.ParameterAttributes.None, "il");
            mb_GetTranspiler.DefineParameter(1, System.Reflection.ParameterAttributes.None, "orig");
            mb_GetTranspiler.DefineParameter(2, System.Reflection.ParameterAttributes.None, "instrs");
            ILGenerator il = mb_GetTranspiler.GetILGenerator();

            il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, fb_Instance);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            il.Emit(System.Reflection.Emit.OpCodes.Callvirt, m_t_MMHarmonyTranspilerProxy_Init);

            il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, fb_Instance);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            t_getter = tb_getter.CreateType();
            t_getter.GetField(fb_Instance.Name).SetValue(null, transpiler);
            return t_getter.GetMethod(mb_GetTranspiler.Name);
        }

        private static bool _Initialized = false;
        public static void Init() {
            if (_Initialized)
                return;

            Harmony = _FindHarmony();
            if (Harmony == null)
                throw new InvalidOperationException("Cannot use HarmonyHelper when Harmony not loaded!");

            t_HarmonyInstance = Harmony.GetType("Harmony.HarmonyInstance", true);
            t_HarmonyMethod = Harmony.GetType("Harmony.HarmonyMethod", true);
            t_CodeInstruction = Harmony.GetType("Harmony.CodeInstruction", true);

            HarmonyProxy = _GenerateMMHarmonyProxy();
            t_MMHarmonyTranspilerProxy = HarmonyProxy.GetType("MonoMod.HarmonyCompat.MMHarmonyTranspilerProxy", true);
            m_t_MMHarmonyTranspilerProxy_Init = t_MMHarmonyTranspilerProxy.GetMethod("Init");

            HarmonyRuntime = _GenerateMMHarmonyRuntime();

            _CreateInstance = t_HarmonyInstance.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).GetDelegate();
            _PatchTranspiler = t_HarmonyInstance.GetMethod("Patch", BindingFlags.Public | BindingFlags.Instance).GetDelegate();

            _NewHarmonyMethod = t_HarmonyMethod.GetConstructor(new Type[] { typeof(MethodInfo) }).GetDelegate();

            _NewTranspilerProxy = t_MMHarmonyTranspilerProxy.GetConstructor(new Type[] { typeof(MMHarmonyTranspiler) }).GetDelegate();

            _Initialized = true;
        }


        private static Assembly _FindHarmony() {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++) {
                Assembly asm = asms[i];
                if (asm.GetName().Name == "0Harmony" || asm.GetName().Name == "Harmony" ||
                    asm.GetType("Harmony.HarmonyInstance") != null) {
                    return asm;
                }
            }
            return Assembly.Load("0Harmony");
        }


        private static ModuleBuilder _GenerateMMHarmonyRuntime() {
            AssemblyBuilder asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("MonoModHarmonyRuntime -Dynamic"), AssemblyBuilderAccess.Run);
            ModuleBuilder modBuilder = asmBuilder.DefineDynamicModule("MonoModHarmonyRuntime -Dynamic");
            return modBuilder;
        }


        private static Assembly _GenerateMMHarmonyProxy() {
            if (HarmonyProxy != null)
                return HarmonyProxy;

            ModuleDefinition proxyMod = ModuleDefinition.CreateModule(
                $"MonoModHarmonyProxy -MMILRT",
                new ModuleParameters() {
                    Kind = ModuleKind.Dll,
                    Runtime = TargetRuntime.Net_2_0
                }
            );

            _GenerateMMHarmonyTranspilerProxy(proxyMod);

            using (MemoryStream asmStream = new MemoryStream()) {
                proxyMod.Write(asmStream);
                HarmonyProxy = Assembly.Load(asmStream.GetBuffer());
            }

            AppDomain.CurrentDomain.AssemblyResolve +=
                (s, e) => e.Name == HarmonyProxy.FullName ? HarmonyProxy : null;

            /**//*
            using (FileStream debugStream = File.OpenWrite(Path.Combine(
                ".", "MonoModHarmonyProxy-MMILRT.dll")))
                            proxyMod.Write(debugStream);
            /**/

            return HarmonyProxy;
        }

        private static void _GenerateMMHarmonyTranspilerProxy(ModuleDefinition proxyMod) {
            TypeReference tr_CodeInstruction = proxyMod.ImportReference(t_CodeInstruction);
            TypeReference tr_MMHarmonyTranspiler = proxyMod.ImportReference(typeof(MMHarmonyTranspiler));
            TypeReference tr_MMHarmonyInstruction = proxyMod.ImportReference(typeof(MMHarmonyInstruction));

            TypeReference tr_IEnumerable_CodeInstruction = proxyMod.ImportReference(typeof(IEnumerable<>).MakeGenericType(t_CodeInstruction));
            TypeReference tr_IEnumerable = proxyMod.ImportReference(typeof(IEnumerable));

            TypeReference tr_IEnumerator_CodeInstruction = proxyMod.ImportReference(typeof(IEnumerator<>).MakeGenericType(t_CodeInstruction));
            TypeReference tr_IEnumerator = proxyMod.ImportReference(typeof(IEnumerator));

            TypeDefinition type = new TypeDefinition("MonoMod.HarmonyCompat", "MMHarmonyTranspilerProxy", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed) {
                BaseType = proxyMod.TypeSystem.Object,
                Interfaces = {
                    new InterfaceImplementation(tr_IEnumerable_CodeInstruction),
                    new InterfaceImplementation(tr_IEnumerable),
                    new InterfaceImplementation(tr_IEnumerator_CodeInstruction),
                    new InterfaceImplementation(tr_IEnumerator),
                    new InterfaceImplementation(proxyMod.ImportReference(typeof(IDisposable)))
                }
            };
            proxyMod.Types.Add(type);

            MethodBody body;
            ILProcessor il;


            FieldDefinition f_Transpiler = new FieldDefinition("Transpiler", FieldAttributes.Public, tr_MMHarmonyTranspiler);
            type.Fields.Add(f_Transpiler);


            MethodDefinition m_ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, proxyMod.TypeSystem.Void);
            m_ctor.Parameters.Add(new ParameterDefinition("transpiler", ParameterAttributes.None, tr_MMHarmonyTranspiler));
            type.Methods.Add(m_ctor);
            body = m_ctor.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, f_Transpiler);

            il.Emit(OpCodes.Ret);


            MethodDefinition m_Current_gen = new MethodDefinition("get_Current", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, tr_CodeInstruction);
            type.Methods.Add(m_Current_gen);
            body = m_Current_gen.Body;
            il = body.GetILProcessor();

            body.Variables.Add(new VariableDefinition(tr_MMHarmonyInstruction));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, f_Transpiler);
            il.Emit(OpCodes.Call, proxyMod.ImportReference(typeof(MMHarmonyTranspiler).GetMethod("get_Current", BindingFlags.Public | BindingFlags.Instance)));
            il.Emit(OpCodes.Stloc_0);

            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldfld, proxyMod.ImportReference(typeof(MMHarmonyInstruction).GetField("opcode")));

            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldfld, proxyMod.ImportReference(typeof(MMHarmonyInstruction).GetField("operand")));

            il.Emit(OpCodes.Newobj, proxyMod.ImportReference(t_CodeInstruction.GetConstructor(new Type[] { typeof(System.Reflection.Emit.OpCode), typeof(object) })));

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldfld, proxyMod.ImportReference(typeof(MMHarmonyInstruction).GetField("labels")));
            il.Emit(OpCodes.Stfld, proxyMod.ImportReference(t_CodeInstruction.GetField("labels")));

            il.Emit(OpCodes.Ret);


            PropertyDefinition p_Current_gen = new PropertyDefinition("Current", PropertyAttributes.None, m_Current_gen.ReturnType);
            p_Current_gen.GetMethod = m_Current_gen;
            type.Properties.Add(p_Current_gen);


            MethodDefinition m_Current = new MethodDefinition("System.Collections.IEnumerator.get_Current", MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, proxyMod.TypeSystem.Object);
            m_Current.Overrides.Add(proxyMod.ImportReference(typeof(IEnumerator).GetMethod("get_Current", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)));
            type.Methods.Add(m_Current);
            body = m_Current.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, m_Current_gen);
            il.Emit(OpCodes.Ret);

            PropertyDefinition p_Current = new PropertyDefinition("System.Collections.IEnumerator.Current", PropertyAttributes.None, m_Current.ReturnType);
            p_Current.GetMethod = m_Current;
            type.Properties.Add(p_Current);


            MethodDefinition m_MoveNext = new MethodDefinition("MoveNext", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, proxyMod.TypeSystem.Boolean);
            type.Methods.Add(m_MoveNext);
            body = m_MoveNext.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, f_Transpiler);
            il.Emit(OpCodes.Callvirt, proxyMod.ImportReference(typeof(MMHarmonyTranspiler).GetMethod("MoveNext")));
            il.Emit(OpCodes.Ret);


            MethodDefinition m_Reset = new MethodDefinition("Reset", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, proxyMod.TypeSystem.Void);
            type.Methods.Add(m_Reset);
            body = m_Reset.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, f_Transpiler);
            il.Emit(OpCodes.Callvirt, proxyMod.ImportReference(typeof(MMHarmonyTranspiler).GetMethod("Reset")));
            il.Emit(OpCodes.Ret);


            MethodDefinition m_Dispose = new MethodDefinition("Dispose", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, proxyMod.TypeSystem.Void);
            type.Methods.Add(m_Dispose);
            body = m_Dispose.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, f_Transpiler);
            il.Emit(OpCodes.Callvirt, proxyMod.ImportReference(typeof(MMHarmonyTranspiler).GetMethod("Dispose")));
            il.Emit(OpCodes.Ret);


            MethodDefinition m_GetEnumerator_gen = new MethodDefinition("GetEnumerator", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, tr_IEnumerator_CodeInstruction);
            type.Methods.Add(m_GetEnumerator_gen);
            body = m_GetEnumerator_gen.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);


            MethodDefinition m_GetEnumerator = new MethodDefinition("System.Collections.IEnumerable.GetEnumerator", MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, tr_IEnumerator);
            m_GetEnumerator.Overrides.Add(proxyMod.ImportReference(typeof(IEnumerable).GetMethod("GetEnumerator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)));
            type.Methods.Add(m_GetEnumerator);
            body = m_GetEnumerator.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);


            MethodDefinition m_Init = new MethodDefinition("Init", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Virtual, proxyMod.TypeSystem.Void);
            m_Init.Parameters.Add(new ParameterDefinition("il", ParameterAttributes.None, proxyMod.ImportReference(typeof(ILGenerator))));
            m_Init.Parameters.Add(new ParameterDefinition("orig", ParameterAttributes.None, proxyMod.ImportReference(typeof(MethodBase))));
            m_Init.Parameters.Add(new ParameterDefinition("instrs", ParameterAttributes.None, tr_IEnumerable_CodeInstruction));
            type.Methods.Add(m_Init);
            body = m_Init.Body;
            il = body.GetILProcessor();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, f_Transpiler);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, proxyMod.ImportReference(typeof(MMHarmonyTranspiler).GetMethod("Init")));
            il.Emit(OpCodes.Ret);
        }


    }
}
