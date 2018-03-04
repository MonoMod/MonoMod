using Mono.Cecil;
using StringInject;
using System;
using MonoMod.NET40Shim;
using MonoMod.Helpers;

namespace MonoMod.InlineRT {
    public static partial class MonoModRule {

        public static MonoModder Modder {
            get {
                return MonoModRulesManager.Modder;
            }
        }


        public static void RelinkModule(string from, string toName) {
            MonoModder self = Modder;

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

        public static void RelinkType(string from, string to) {
            MonoModder self = Modder;

            from = from.Inject(MonoModder.Data);
            to = to.Inject(MonoModder.Data);

            self.Log($"[MonoModRules] RelinkType: {from} -> {to}");
            self.RelinkMap[from] = to;
        }

        public static void RelinkMember(string from, string toType, string toMember) {
            MonoModder self = Modder;

            from = from.Inject(MonoModder.Data);
            toType = toType.Inject(MonoModder.Data);
            toMember = toMember.Inject(MonoModder.Data);

            self.Log($"[MonoModRules] RelinkMember: {from} -> {toType}::{toMember}");
            self.RelinkMap[from] = new RelinkMapEntry(toType, toMember);
        }


        public static void Patch(string id, bool patch) {
            MonoModder self = Modder;

            id = id.Inject(MonoModder.Data);
                
            self.Log($"[MonoModRules] Patch: {id}: {patch}");
            if (patch && self.SkipList.Contains(id))
                self.SkipList.Remove(id);
            else if (!patch && !self.SkipList.Contains(id))
                self.SkipList.Add(id);
        }


        public static void RegisterCustomAttribute(string attribName, string handlerName) {
            MonoModder self = Modder;

            self.CustomAttributeHandlers[attribName] = MonoModRulesManager.RuleType.GetMethod(handlerName).GetDelegate();
        }

        public static void RegisterCustomMethodAttribute(string attribName, string handlerName) {
            MonoModder self = Modder;

            self.CustomMethodAttributeHandlers[attribName] = MonoModRulesManager.RuleType.GetMethod(handlerName).GetDelegate();
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
