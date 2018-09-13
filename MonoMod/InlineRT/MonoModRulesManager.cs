using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;

namespace MonoMod.InlineRT {
    public static class MonoModRulesManager {

        public static Dictionary<long, WeakReference> ModderMap = new Dictionary<long, WeakReference>();
        public static ObjectIDGenerator ModderIdGen = new ObjectIDGenerator();

        private static Assembly MonoModAsm = Assembly.GetExecutingAssembly();

        public static MonoModder Modder {
            get {
                StackTrace st = new StackTrace();
                for (int i = 1; i < st.FrameCount; i++) {
                    StackFrame frame = st.GetFrame(i);
                    MethodBase method = frame.GetMethod();
                    Assembly asm = method.DeclaringType.Assembly;
                    if (asm == MonoModAsm)
                        continue;
                    MonoModder modder = GetModder(method.DeclaringType.Assembly.GetName().Name);
                    if (modder != null)
                        return modder;
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
                    if (asm != MonoModAsm)
                        return method.DeclaringType;
                }
                return null;
            }
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
            int idIndex = idString.IndexOf("[MMILRT, ID:");
            if (idIndex == -1)
                return null;
            idString = idString.Substring(idIndex + 12);
            idString = idString.Substring(0, idString.IndexOf(']'));
            long id;
            if (!long.TryParse(idString, out id))
                throw new InvalidOperationException($"Cannot get MonoModder ID from assembly name {asmName}");
            WeakReference modder;
            if (!ModderMap.TryGetValue(id, out modder) || !modder.IsAlive)
                return null;
            return (MonoModder) modder.Target;
        }

        public static Type ExecuteRules(this MonoModder self, TypeDefinition orig) {
            ModuleDefinition scope = (ModuleDefinition) orig.Scope;
            if (!self.DependencyMap.ContainsKey(scope)) {
                // Runtime relinkers can parse rules by passing the "rule module" directly.
                // Unfortunately, it bypasses the "MonoMod split upgrade hack."
                // This hack fixes that hack.
                self.MapDependencies(scope);
                // Don't add scope to Mods, as that'd affect any further patching passes.
            }

            ModuleDefinition wrapper = ModuleDefinition.CreateModule(
                $"{orig.Module.Name.Substring(0, orig.Module.Name.Length - 4)}.MonoModRules [MMILRT, ID:{GetId(self)}]",
                new ModuleParameters() {
                    Architecture = orig.Module.Architecture,
                    AssemblyResolver = self.AssemblyResolver,
                    Kind = ModuleKind.Dll,
                    MetadataResolver = orig.Module.MetadataResolver,
                    Runtime = TargetRuntime.Net_2_0
                }
            );
            MonoModder wrapperMod = new MonoModRulesModder() {
                Module = wrapper,
                Orig = orig,

                CleanupEnabled = false,

                DependencyDirs = self.DependencyDirs,
                MissingDependencyResolver = self.MissingDependencyResolver
            };
            wrapperMod.WriterParameters.WriteSymbols = false;
            wrapperMod.WriterParameters.SymbolWriterProvider = null;

            bool missingDependencyThrow = self.MissingDependencyThrow;
            self.MissingDependencyThrow = false;

            // Copy all dependencies.
            wrapper.AssemblyReferences.AddRange(scope.AssemblyReferences);
            wrapperMod.DependencyMap[wrapper] = new List<ModuleDefinition>(self.DependencyMap[scope]);

            // Only add a copy of the map - adding the MMILRT asm itself to the map only causes issues.
            wrapperMod.DependencyCache.AddRange(self.DependencyCache);
            foreach (KeyValuePair<ModuleDefinition, List<ModuleDefinition>> mapping in self.DependencyMap)
                wrapperMod.DependencyMap[mapping.Key] = new List<ModuleDefinition>(mapping.Value);

            wrapperMod.Mods.AddRange(self.Mods);
            // Required as the relinker only deep-relinks if the method the type comes from is a mod.
            // Fixes nasty reference import sharing issues.
            wrapperMod.Mods.Add(self.Module);

            wrapperMod.PrePatchType(orig, forceAdd: true);
            wrapperMod.PatchType(orig);
            TypeDefinition rulesCecil = wrapper.GetType(orig.FullName);
            // wrapperMod.PatchRefsInType(rulesCecil);
            wrapperMod.PatchRefs(); // Runs any special passes in-between, f.e. upgrading from pre-split to post-split.

            Assembly asm;
            using (MemoryStream asmStream = new MemoryStream()) {
                wrapperMod.Write(asmStream);
                asm = Assembly.Load(asmStream.GetBuffer());
            }

            /*/
            using (FileStream debugStream = File.OpenWrite(Path.Combine(
                self.DependencyDirs[0], $"{orig.Module.Name.Substring(0, orig.Module.Name.Length - 4)}.MonoModRules-MMILRT.dll")))
                wrapperMod.Write(debugStream);
            /**/

            self.MissingDependencyThrow = missingDependencyThrow;

            Type rules = asm.GetType(orig.FullName);
            RuntimeHelpers.RunClassConstructor(rules.TypeHandle);

            return rules;
        }

    }
}
