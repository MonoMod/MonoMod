using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.RuntimeDetour {
    public static class HarmonyDetourBridge {

        public enum BridgeType {
            Basic,
            Integrated
        }

        private static readonly BindingFlags _FlagsHarmonyAll =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
#if !NETSTANDARD1_X
            BindingFlags.GetField |
            BindingFlags.SetField |
            BindingFlags.GetProperty |
            BindingFlags.SetProperty |
#endif
        0;

        public static bool Initialized { get; private set; }
        private static BridgeType CurrentType;

        private static Assembly _ASM;
        private static readonly List<IDetour> _Detours = new List<IDetour>();

        private static Type t_PatchInfo;
        private static FieldInfo f_PatchInfo_prefixes;
        private static FieldInfo f_PatchInfo_postfixes;
        private static FieldInfo f_PatchInfo_transpilers;
        private static FieldInfo f_PatchInfo_finalizers;

        [ThreadStatic]
        private static DynamicMethodDefinition _LastWrapperDMD;

        public static bool Init(bool forceLoad = true, BridgeType type = BridgeType.Basic) {
            if (_ASM == null)
                _ASM = _FindHarmony();
            if (_ASM == null && forceLoad)
                _ASM = Assembly.Load(new AssemblyName() {
                    Name = "0Harmony"
                });
            if (_ASM == null)
                return false;

            if (Initialized)
                return true;
            Initialized = true;

            CurrentType = type;

            t_PatchInfo = _ASM.GetType("Harmony.PatchInfo") ?? _ASM.GetType("HarmonyLib.PatchInfo");
            f_PatchInfo_prefixes = t_PatchInfo.GetField("prefixes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            f_PatchInfo_postfixes = t_PatchInfo.GetField("postfixes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            f_PatchInfo_transpilers = t_PatchInfo.GetField("transpilers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            f_PatchInfo_finalizers = t_PatchInfo.GetField("finalizers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (MethodInfo methodRD in typeof(HarmonyDetourBridge).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)) {
                foreach (DetourToRDAttribute info in methodRD.GetCustomAttributes(typeof(DetourToRDAttribute), false)) {
                    MethodInfo methodH = GetHarmonyMethod(methodRD, info.Type, info.SkipParams, info.Name);
                    if (methodH == null)
                        continue;
                    _Detours.Add(new Hook(methodH, methodRD));
                }

                foreach (DetourToHAttribute info in methodRD.GetCustomAttributes(typeof(DetourToHAttribute), false)) {
                    MethodInfo methodH = GetHarmonyMethod(methodRD, info.Type, info.SkipParams, info.Name);
                    if (methodH == null)
                        continue;
                    _Detours.Add(new Detour(methodRD, methodH));
                }

                foreach (TranspileAttribute info in methodRD.GetCustomAttributes(typeof(DetourToHAttribute), false)) {
                    MethodInfo methodH = GetHarmonyMethod(methodRD, info.Type, -1, info.Name);
                    if (methodH == null)
                        continue;
                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(methodH)) {
                        using (ILContext il = new ILContext(dmd.Definition))
                            il.Invoke((ILContext.Manipulator) methodH.CreateDelegate<ILContext.Manipulator>());
                        _Detours.Add(new Detour(methodH, dmd.Generate()));
                    }
                }
            }

            return true;
        }

        public static void Reset() {
            if (!Initialized)
                return;
            Initialized = false;

            foreach (Detour detour in _Detours)
                detour.Dispose();
            _Detours.Clear();
        }

        private static MethodInfo GetHarmonyMethod(MethodInfo ctx, string typeName, int skipParams, string name) {
            Type type = _ASM.GetType("Harmony." + typeName) ?? _ASM.GetType("HarmonyLib." + typeName);
            if (type == null)
                return null;

            if (skipParams < 0)
                return type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(method => method.Name == name)
                    .ElementAtOrDefault(-skipParams);

#if NETSTANDARD1_X
            return type
                .GetMethod(
                    !string.IsNullOrEmpty(name) ? name : ctx.Name,
                    ctx.GetParameters().Skip(skipParams).Select(p => p.ParameterType).ToArray()
                );
#else
            return type
                .GetMethod(
                    !string.IsNullOrEmpty(name) ? name : ctx.Name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                    null,
                    ctx.GetParameters().Skip(skipParams).Select(p => p.ParameterType).ToArray(),
                    null
                );
#endif
        }

        // Replacement for DynamicTools.CreateDynamicMethod
        private static DynamicMethodDefinition CreateDMD(MethodBase original, string suffix) {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            ParameterInfo[] args = original.GetParameters();
            Type[] argTypes;
            if (!original.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = original.GetThisParamType();
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            return new DynamicMethodDefinition(
                (original.Name + suffix).Replace("<>", ""),
                (original as MethodInfo)?.ReturnType ?? typeof(void),
                argTypes
            );
        }

        [DetourToRD("Memory")]
        private static long GetMethodStart(MethodBase method, out Exception exception) {
            exception = null;
            try {
                return (long) method.GetNativeStart();
            } catch (Exception e) {
                exception = e;
                return 0;
            }
        }

        [DetourToRD("Memory")]
        private static string WriteJump(long memory, long destination) {
            _Detours.Add(new NativeDetour((IntPtr) memory, (IntPtr) destination));
            return null;
        }

        [DetourToRD("Memory")]
        private static string DetourMethod(MethodBase original, MethodBase replacement) {
            _Detours.Add(new Detour(original, replacement));
            return null;
        }

        [DetourToRD("PatchProcessor", 2)]
        private static List<System.Reflection.Emit.DynamicMethod> Patch(Func<object, List<System.Reflection.Emit.DynamicMethod>> orig, object self) {
            orig(self);

            // We can't instantiate DMs.
            // Even if we could return generated DMDs, they don't always result in DMs.
            // Thus, return an empty list and hope for the best.
            return new List<System.Reflection.Emit.DynamicMethod>();
        }

        // Patch and both Unpatch methods run UpdateWrapper, which then runs CreatePatchedMethod.

        // TODO: Does NativeThisPointer.NeedsNativeThisPointerFix need to be patched?

        [Transpile("MethodPatcher")]
        private static void CreatePatchedMethod(ILContext il) {
            ILCursor c = new ILCursor(il);

            // The original method uses System.Reflection.Emit.

            // Find and replace DynamicTools.CreateDynamicMethod
            c.GotoNext(i => i.MatchCall("Harmony.DynamicTools", "CreateDynamicMethod"));
            c.Next.OpCode = OpCodes.Call;
            c.Next.Operand = il.Import(typeof(HarmonyDetourBridge).GetMethod("CreateDMD", BindingFlags.NonPublic | BindingFlags.Static));
            
            // Find the variable holding the "dynamic method" and update it.
            int varDMDi = -1;
            c.GotoNext(i => i.MatchStloc(out varDMDi));
            VariableDefinition varDMD = il.Body.Variables[varDMDi];
            varDMD.VariableType = il.Import(typeof(DynamicMethodDefinition));

            // Find and replace patch.GetILGenerator
            c.GotoNext(i => i.MatchCallvirt<System.Reflection.Emit.DynamicMethod>("GetILGenerator"));
            c.Next.OpCode = OpCodes.Call;
            c.Next.Operand = il.Import(typeof(DynamicMethodDefinition).GetMethod("GetILGenerator", BindingFlags.Public | BindingFlags.Static));

            // Find and remove DynamicTools.PrepareDynamicMethod
            c.GotoNext(i => i.MatchCall("Harmony.DynamicTools", "PrepareDynamicMethod"));
            c.Next.OpCode = OpCodes.Pop;
            c.Next.Operand = null;

            // Go to the next ldloc that loads the DynamicMethod.
            // No matter if it gets stored into a local variable or returned immediately,
            // grab it, store it separately and push null as a replacement.
            c.GotoNext(i => i.MatchLdloc(varDMDi));
            c.Index++;
            c.EmitDelegate<Func<DynamicMethodDefinition, System.Reflection.Emit.DynamicMethod>>(dmd => {
                _LastWrapperDMD = dmd;
                return null;
            });
        }

        private class DetourToRDAttribute : Attribute {
            public string Type { get; }
            public int SkipParams { get; }
            public string Name { get; }
            public DetourToRDAttribute(string type, int skipParams = 0, string name = null) {
                Type = type;
                SkipParams = skipParams;
                Name = name;
            }
        }

        private class DetourToHAttribute : Attribute {
            public string Type { get; }
            public int SkipParams { get; }
            public string Name { get; }
            public DetourToHAttribute(string type, int skipParams = 0, string name = null) {
                Type = type;
                SkipParams = skipParams;
                Name = name;
            }
        }

        private class TranspileAttribute : Attribute {
            public string Type { get; }
            public string Name { get; }
            public TranspileAttribute(string type, string name = null) {
                Type = type;
                Name = name;
            }
        }

        private static Assembly _FindHarmony() {
#if !NETSTANDARD1_X
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.GetName().Name == "0Harmony" || asm.GetName().Name == "Harmony" ||
                    asm.GetType("Harmony.HarmonyInstance") != null) {
                    return asm;
                }
            }
#endif
            return Type.GetType("Harmony.HarmonyInstance", false, false)?.GetTypeInfo()?.Assembly;
        }

    }
}
