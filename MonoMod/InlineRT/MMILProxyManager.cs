using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace MonoMod.InlineRT {
    public static class MMILProxyManager {

        public static IDictionary<long, WeakReference> ModderMap = new FastDictionary<long, WeakReference>();
        public static ObjectIDGenerator ModderIdGen = new ObjectIDGenerator();

        private static Assembly MonoModAsm = Assembly.GetExecutingAssembly();
        private static Type t_MMIL = typeof(MMIL);
        private static Type t_MMILRT = typeof(MMILRT);
        private static MethodInfo m_get_Self = typeof(MMILProxyManager).GetProperty("Self").GetGetMethod();

        public static Type MMILProxy = GenerateMMILProxy();

        public static MonoModder Self {
            get {
                StackTrace st = new StackTrace();
                for (int i = 1; i < st.FrameCount; i++) {
                    StackFrame frame = st.GetFrame(i);
                    MethodBase method = frame.GetMethod();
                    Assembly asm = method.DeclaringType.Assembly;
                    if (asm != MonoModAsm &&
                        asm != MMILProxy.Assembly)
                        return GetModder(method.DeclaringType.Assembly.GetName().Name);
                }
                return null;
            }
        }

        public static Type RuleType {
            get {
                StackTrace st = new StackTrace();
                for (int i = 1; i < st.FrameCount; i++) {
                    StackFrame frame = st.GetFrame(i);
                    MethodBase method = frame.GetMethod();
                    Assembly asm = method.DeclaringType.Assembly;
                    if (asm != MonoModAsm &&
                        asm != MMILProxy.Assembly)
                        return method.DeclaringType;
                }
                return null;
            }
        }

        public static Type GenerateMMILProxy() {
            if (MMILProxy != null)
                return MMILProxy;

            ModuleDefinition proxyMod = ModuleDefinition.CreateModule(
                $"{MonoModAsm.GetName().Name}.MMILProxy -MMILRT",
                new ModuleParameters() {
                    Kind = ModuleKind.Dll,
                    Runtime = TargetRuntime.Net_2_0
                }
            );

            TypeDefinition proxy = new TypeDefinition("MonoMod.InlineRT", "MMILProxy", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed) {
                BaseType = proxyMod.TypeSystem.Object
            };
            proxyMod.Types.Add(proxy);

            FillMMILProxy(proxyMod, proxy, t_MMILRT);

            Assembly proxyAsm;
            using (MemoryStream asmStream = new MemoryStream()) {
                proxyMod.Write(asmStream);
                proxyAsm = Assembly.Load(asmStream.GetBuffer());
            }

            AppDomain.CurrentDomain.AssemblyResolve +=
                (s, e) => e.Name == MMILProxy.Assembly.FullName ? MMILProxy.Assembly : null;

            /**//*
            using (FileStream debugStream = File.OpenWrite(Path.Combine(
                ".", $"{MonoModAsm.GetName().Name}.MMMILProxy-MMILRT.dll")))
                            proxyMod.Write(debugStream);
            /**/

            MMILProxy = proxyAsm.GetType(proxy.FullName);
            RuntimeHelpers.RunClassConstructor(MMILProxy.TypeHandle);
            return MMILProxy;
        }

        public static void FillMMILProxy(ModuleDefinition proxyMod, TypeDefinition proxy, Type type) {
            if (type == t_MMILRT) {
                foreach (MethodInfo stub in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (stub.Name != "get_Modder")
                        FillMMILProxy(proxyMod, proxy, stub);

            } else {
                TypeDefinition nested = new TypeDefinition(null, type.Name, TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed) {
                    BaseType = proxyMod.TypeSystem.Object,
                    DeclaringType = proxy
                };
                proxy.NestedTypes.Add(nested);
                proxy = nested;

                foreach (MethodInfo stub in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    FillMMILProxy(proxyMod, proxy, stub);
            }

            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
                FillMMILProxy(proxyMod, proxy, nested);
        }

        public static void FillMMILProxy(ModuleDefinition proxyMod, TypeDefinition proxy, MethodInfo stub) {
            MethodDefinition method = new MethodDefinition(
                stub.Name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static,
                proxyMod.ImportReference(stub.ReturnType)
            );

            ParameterInfo[] args = stub.GetParameters();
            int argsCount = args.Length;
            for (int i = 1; i < argsCount; i++)
                method.Parameters.Add(
                    new ParameterDefinition(args[i].Name, (ParameterAttributes) args[i].Attributes, proxyMod.ImportReference(args[i].ParameterType))
                );
            --argsCount; // MMILRT contains self as first parameter

            MethodBody body = method.Body = new MethodBody(method);
            ILProcessor il = body.GetILProcessor();

            // Always required to get the MonoModder instance.
            il.Emit(OpCodes.Call, proxyMod.ImportReference(m_get_Self));

            // Load arguments to stack.
            // TODO: What about generic arguments in MMILProxy?
            if (argsCount > 0) il.Emit(OpCodes.Ldarg_0);
            if (argsCount > 1) il.Emit(OpCodes.Ldarg_1);
            if (argsCount > 2) il.Emit(OpCodes.Ldarg_2);
            if (argsCount > 3) il.Emit(OpCodes.Ldarg_3);
            for (int i = 4; i < argsCount && i < 256; i++)
                il.Emit(OpCodes.Ldarg_S, (byte) i);
            for (int i = 256; i < argsCount; i++)
                il.Emit(OpCodes.Ldarg, i);

            // Call the actual method.
            il.Emit(OpCodes.Call, proxyMod.ImportReference(stub));

            // Finish it off with a ret.
            il.Emit(OpCodes.Ret);

            proxy.Methods.Add(method);
        }

        public static void Register(MonoModder self) {
            bool firstTime;
            ModderMap[ModderIdGen.GetId(self, out firstTime)] = new WeakReference(self);
            if (!firstTime)
                throw new InvalidOperationException("MonoModder instance already registered in MMILProxyManager");
        }

        public static long GetId(MonoModder self) {
            bool firstTime;
            long id = ModderIdGen.GetId(self, out firstTime);
            if (firstTime)
                throw new InvalidOperationException("MonoModder instance wasn't registered in MMILProxyManager");
            return id;
        }

        public static MonoModder GetModder(string asmName) {
            string idString = asmName;
            idString = idString.Substring(idString.IndexOf("-ID:") + 4) + ' ';
            idString = idString.Substring(0, idString.IndexOf(' '));
            long id;
            if (!long.TryParse(idString, out id))
                throw new InvalidOperationException($"Cannot get MonoModder ID from assembly name {asmName}");
            return (MonoModder) ModderMap[id].Target;
        }


        public static TypeReference RelinkToProxy(MonoModder self, TypeReference orig)
            => self.Module.ImportReference(MMILProxy.Assembly.GetType(
                $"{MMILProxy.FullName}{orig.FullName.Substring(4)}"
                    .Replace('/', '+')
            ));

        public static bool IsMMILType(this TypeReference type) {
            while (type.DeclaringType != null)
                type = type.DeclaringType;
            return type.FullName == "MMIL";
        }

    }
}
