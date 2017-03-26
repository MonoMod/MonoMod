using Mono.Cecil;
using StringInject;
using System;
using MonoMod.NET40Shim;

namespace MonoMod.InlineRT {
    public static partial class MMILRT {

        // MMILRT.Modder is easier to understand than MMILProxyManager.Self.
        public static MonoModder Modder {
            get {
                return MMILProxyManager.Self;
            }
        }

        public static class Rule {

            public static void RelinkModule(MonoModder self, string from, string toName) {
                from = from.Inject(MonoModder.Data);
                toName = toName.Inject(MonoModder.Data);

                bool retrying = false;
                ModuleDefinition to = null;
                RETRY:
                if (toName + ".dll" == self.Module.Name)
                    to = self.Module;
                else if (self.DependencyCache.TryGetValue(toName, out to)) { } else if (!retrying) {
                    self.MapDependency(self.Module, toName);
                    retrying = true;
                    goto RETRY;
                }

                if (to != null) {
                    self.Log($"[MonoModRules] RelinkModule: {from} -> {toName}");
                    self.RelinkModuleMap[from] = to;
                }
            }

            public static void RelinkType(MonoModder self, string from, string to) {
                from = from.Inject(MonoModder.Data);
                to = to.Inject(MonoModder.Data);

                self.Log($"[MonoModRules] RelinkType: {from} -> {to}");
                self.RelinkMap[from] = to;
            }

            public static void RelinkMember(MonoModder self, string from, string toType, string toMember) {
                from = from.Inject(MonoModder.Data);
                toType = toType.Inject(MonoModder.Data);
                toMember = toMember.Inject(MonoModder.Data);

                self.Log($"[MonoModRules] RelinkMember: {from} -> {toType}::{toMember}");
                self.RelinkMap[from] = Tuple.Create(toType, toMember);
            }


            public static void Patch(MonoModder self, string id, bool patch) {
                id = id.Inject(MonoModder.Data);
                
                self.Log($"[MonoModRules] Patch: {id}: {patch}");
                if (patch && self.SkipList.Contains(id))
                    self.SkipList.Remove(id);
                else if (!patch && !self.SkipList.Contains(id))
                    self.SkipList.Add(id);
            }


            public static void RegisterCustomAttribute(MonoModder self, string attribName, string handlerName) {
                self.CustomAttributeHandlers[attribName] = MMILProxyManager.RuleType.GetMethod(handlerName).GetDelegate();
            }

            public static void RegisterCustomMethodAttribute(MonoModder self, string attribName, string handlerName) {
                self.CustomMethodAttributeHandlers[attribName] = MMILProxyManager.RuleType.GetMethod(handlerName).GetDelegate();
            }

        }

        public static class Flag {

            public static bool Get(MonoModder self, string k) => MonoModder.Data[k] as bool? ?? false;
            public static void Set(MonoModder self, string k, bool v) => MonoModder.Data[k] = v;

        }

        public static class Data {

            public static object Get(MonoModder self, string k) => MonoModder.Data[k];
            public static void Set(MonoModder self, string k, object v) => MonoModder.Data[k] = v;

        }

    }
}
