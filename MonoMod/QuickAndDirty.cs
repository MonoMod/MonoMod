using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using Mono.Collections.Generic;

//quick, dirty code to do dirty code stuff
namespace MonoMod {
    static class Feed {
        
        private static void p(TypeDefinition t) {
            //Replace with your own code
            if (!t.Name.StartsWith("Menu") && !t.Name.StartsWith("SelectorPhase")) {
                return;
            }
            
            t.IsNotPublic = false;
            t.IsPublic = true;
            
            foreach (FieldDefinition f in t.Fields) {
                if (f.IsSpecialName || f.IsRuntimeSpecialName) {
                    continue;
                }
                f.IsPrivate = false;
                f.IsPublic = true;
            }
            
            foreach (MethodDefinition m in t.Methods) {
                m.IsPrivate = false;
                m.IsPublic = true;
            }
            
            foreach (TypeDefinition nt in t.NestedTypes) {
                p(nt);
            }
        }
        
        public static void Me(string path) {
            ModuleDefinition mod = ModuleDefinition.ReadModule(path, new ReaderParameters(ReadingMode.Immediate));
            
            foreach (TypeDefinition type in mod.Types) {
                p(type);
            }
            
            mod.Write(path);
        }
        
    }
}
