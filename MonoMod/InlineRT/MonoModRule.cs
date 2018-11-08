using Mono.Cecil;
using MonoMod.Utils;
using System;

#if NETSTANDARD
using static System.Reflection.IntrospectionExtensions;
using static System.Reflection.TypeExtensions;
#endif

namespace MonoMod.InlineRT {
    public static partial class MonoModRule {

        public static MonoModder Modder {
            get {
                return MonoModRulesManager.Modder;
            }
        }

        public static void RelinkModule(string from, string toName) {
            MonoModder self = Modder;

            from = from.Inject(MonoModExt.SharedData);
            toName = toName.Inject(MonoModExt.SharedData);

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

            from = from.Inject(MonoModExt.SharedData);
            to = to.Inject(MonoModExt.SharedData);

            self.Log($"[MonoModRules] RelinkType: {from} -> {to}");
            self.RelinkMap[from] = to;
        }

        public static void RelinkMember(string from, string toType, string toMember) {
            MonoModder self = Modder;

            from = from.Inject(MonoModExt.SharedData);
            toType = toType.Inject(MonoModExt.SharedData);
            toMember = toMember.Inject(MonoModExt.SharedData);

            self.Log($"[MonoModRules] RelinkMember: {from} -> {toType}::{toMember}");
            self.RelinkMap[from] = new RelinkMapEntry(toType, toMember);
        }


        public static void Patch(string id, bool patch) {
            MonoModder self = Modder;

            id = id.Inject(MonoModExt.SharedData);
                
            self.Log($"[MonoModRules] Patch: {id}: {patch}");
            if (patch && self.SkipList.Contains(id))
                self.SkipList.Remove(id);
            else if (!patch && !self.SkipList.Contains(id))
                self.SkipList.Add(id);
        }


        public static void RegisterCustomAttribute(string attribName, string handlerName) {
            MonoModder self = Modder;

            self.CustomAttributeHandlers[attribName] = MonoModRulesManager.RuleType.GetMethod(handlerName).GetFastDelegate();
        }

        public static void RegisterCustomMethodAttribute(string attribName, string handlerName) {
            MonoModder self = Modder;

            self.CustomMethodAttributeHandlers[attribName] = MonoModRulesManager.RuleType.GetMethod(handlerName).GetFastDelegate();
        }

        public static class Flag {

            public static bool Get(string k) => MonoModExt.SharedData[k] as bool? ?? false;
            public static void Set(string k, bool v) => MonoModExt.SharedData[k] = v;

        }

        public static class Data {

            public static object Get(string k) => MonoModExt.SharedData[k];
            public static void Set(string k, object v) => MonoModExt.SharedData[k] = v;

        }

    }
}
