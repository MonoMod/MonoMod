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

        public MMHarmonyInstance(MonoModder modder, Assembly target) {
            HarmonyHelper.Init();
            Modder = modder;
            Harmony = HarmonyHelper.CreateInstance($"monomod.{modder.Module.Assembly.Name.Name}.{MMILProxyManager.GetId(modder)}");
            Target = target;
        }

        public void Log(string str)
            => Modder?.Log("[Harmony] " + str);

        public virtual void PatchAll() {
            foreach (TypeDefinition type in Modder.Module.Types)
                PatchType(type);
        }

        public virtual void PatchType(TypeDefinition type) {
            string typeName = type.FullName;

            Type targetType = Target.GetType(typeName.Replace('/', '+'));
            if (targetType == null) {
                // What to do, what to do...
                Modder.Log($"[PatchType] Type {typeName} not found in target assembly {Target.FullName}");
                return;
            }

            // TODO: [MMHarmony] Dynamically add new fields, methods and properties.
            foreach (MethodDefinition method in type.Methods)
                PatchMethod(targetType, method);
            
            foreach (TypeDefinition nested in type.NestedTypes)
                PatchType(nested);
        }

        public virtual void PatchMethod(Type targetType, MethodDefinition method) {
            string methodName = method.Name;

            MethodBase targetMethod = targetType.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                method.Parameters.Select(p => FindType(p.ParameterType.FullName.Replace('/', '+'))).ToArray(),
                null
            );
            if (targetMethod == null) {
                // What to do, what to do...
                Modder.Log($"[PatchMethod] Method {methodName} not found in target type {targetType.FullName} in {Target.FullName}");
                return;
            }

            // TODO: [Harmony] Parse original code and check if patch needed.
            // byte[] codeOrig = targetMethod.GetMethodBody().GetILAsByteArray();

            Modder.Log($"[PatchMethod] Creating transpiler for {method.FullName} in target assembly {Target.FullName}");
            MMHarmonyTranspiler transpiler = new MMHarmonyTranspiler();
            object transpilerProxy = HarmonyHelper.NewTranspilerProxy(transpiler);
            MethodInfo transpilerGetter = HarmonyHelper.NewTranspilerGetter(transpilerProxy);
            object transpilerHarmonyMethod = HarmonyHelper.NewHarmonyMethod(transpilerGetter);
            HarmonyHelper.PatchTranspiler(Harmony, targetMethod, transpilerHarmonyMethod);
        }

        private readonly static Dictionary<string, Type> _TypeCache = new Dictionary<string, Type>();
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

    }
}
