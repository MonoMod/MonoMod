using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.InlineRT {
    public static class MMILExec {

        public static Type ExecuteRules(this MonoModder self, TypeDefinition orig) {
            ModuleDefinition wrapper = ModuleDefinition.CreateModule(
                $"{orig.Module.Name.Substring(0, orig.Module.Name.Length - 4)}.MonoModRules -ID:{MMILProxyManager.GetId(self)} -MMILRT",
                new ModuleParameters() {
                    Architecture = orig.Module.Architecture,
                    AssemblyResolver = self.AssemblyResolver,
                    Kind = ModuleKind.Dll,
                    MetadataResolver = orig.Module.MetadataResolver,
                    Runtime = orig.Module.Runtime
                }
            );
            MonoModder wrapperMod = new MonoModder() {
                Module = wrapper,

                Logger = msg => self.Log("[MonoModRule] " + msg),

                CleanupEnabled = false,

                DependencyDirs = self.DependencyDirs,
                MissingDependencyResolver = self.MissingDependencyResolver
            };
            wrapperMod.WriterParameters.WriteSymbols = false;
            wrapperMod.WriterParameters.SymbolWriterProvider = null;

            // Only add a copy of the map - adding the MMILRT asm itself to the map only causes issues.
            wrapperMod.DependencyCache.AddRange(self.DependencyCache);
            foreach (KeyValuePair<ModuleDefinition, List<ModuleDefinition>> mapping in self.DependencyMap)
                wrapperMod.DependencyMap[mapping.Key] = new List<ModuleDefinition>(mapping.Value);

            // Required as the relinker only deep-relinks if the method the type comes from is a mod.
            // Fixes nasty reference import sharing issues.
            wrapperMod.Mods.Add(self.Module);

            wrapperMod.Relinker = (mtp, context) =>
                mtp is TypeReference && ((TypeReference) mtp).IsMMILType() ?
                    MMILProxyManager.RelinkToProxy(wrapperMod, (TypeReference) mtp) :
                mtp is TypeReference && ((TypeReference) mtp).FullName == orig.FullName ?
                    wrapper.GetType(orig.FullName) :
                wrapperMod.DefaultRelinker(mtp, context);

            wrapperMod.PrePatchType(orig, forceAdd: true);
            wrapperMod.PatchType(orig);
            TypeDefinition rulesCecil = wrapper.GetType(orig.FullName);
            wrapperMod.PatchRefsInType(rulesCecil);

            Assembly asm;
            using (MemoryStream asmStream = new MemoryStream()) {
                wrapperMod.Write(asmStream);
                asm = Assembly.Load(asmStream.GetBuffer());
            }

            /**//*
            using (FileStream debugStream = File.OpenWrite(Path.Combine(
                self.DependencyDirs[0], $"{orig.Module.Name.Substring(0, orig.Module.Name.Length - 4)}.MonoModRules-MMILRT.dll")))
                wrapperMod.Write(debugStream);
            /**/

            Type rules = asm.GetType(orig.FullName);
            RuntimeHelpers.RunClassConstructor(rules.TypeHandle);

            return rules;
        }

    }
}
