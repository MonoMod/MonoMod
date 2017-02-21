using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.InlineRT {
    public static class MMILExec {

        public static void ExecuteRules(this MonoModder self, TypeDefinition orig) {
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

                DependencyCache = self.DependencyCache,
                DependencyDirs = self.DependencyDirs,
                OnMissingDependency = self.OnMissingDependency,

                Relinker = (mtp, context) =>
                    mtp is MethodReference && ((MethodReference) mtp).DeclaringType.FullName.Contains("MMIL") ?
                    wrapper.ImportReference(MMILProxyManager.Relink(self, (MethodReference) mtp)) :
                    self.Relinker(mtp, context)
            };


            wrapperMod.PrePatchType(orig, forceAdd: true);
            wrapperMod.PatchType(orig);
            TypeDefinition rulesCecil = wrapper.GetType(orig.FullName);
            wrapperMod.PatchRefsInType(rulesCecil);

            Assembly asm;
            using (MemoryStream asmStream = new MemoryStream()) {
                wrapperMod.Write(asmStream);
                asm = Assembly.Load(asmStream.GetBuffer());
            }

            using (FileStream debugStream = File.OpenWrite(Path.Combine(self.DependencyDirs[0], $"{orig.Module.Name.Substring(0, orig.Module.Name.Length - 4)}.MonoModRules-MMILRT.dll"))) {
                wrapperMod.Write(debugStream);
            }

            Type rules = asm.GetType(orig.FullName);
            RuntimeHelpers.RunClassConstructor(rules.TypeHandle);

        }

    }
}
