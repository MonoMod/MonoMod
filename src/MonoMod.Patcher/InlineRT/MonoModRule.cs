using Mono.Cecil;

namespace MonoMod.InlineRT
{
    public static partial class MonoModRule
    {

        public static MonoModder Modder => MonoModRulesManager.Modder;

        public static void RelinkModule(string from, string toName)
        {
            MonoModder self = Modder;

            var retrying = false;
            ModuleDefinition to;
            RETRY:
            if (toName + ".dll" == self.Module.Name)
                to = self.Module;
            else if (self.DependencyCache.TryGetValue(toName, out to)) { }
            else if (!retrying)
            {
                self.MapDependency(self.Module, toName);
                retrying = true;
                goto RETRY;
            }

            if (to != null)
            {
                self.Log($"[MonoModRules] RelinkModule: {from} -> {toName}");
                self.RelinkModuleMap[from] = to;
            }
        }

        public static void RelinkType(string from, string to)
        {
            MonoModder self = Modder;

            self.Log($"[MonoModRules] RelinkType: {from} -> {to}");
            self.RelinkMap[from] = to;
        }

        public static void RelinkMember(string from, string toType, string toMember)
        {
            MonoModder self = Modder;

            self.Log($"[MonoModRules] RelinkMember: {from} -> {toType}::{toMember}");
            self.RelinkMap[from] = new RelinkMapEntry(toType, toMember);
        }


        public static void Patch(string id, bool patch)
        {
            MonoModder self = Modder;

            self.Log($"[MonoModRules] Patch: {id}: {patch}");
            if (patch && self.SkipList.Contains(id))
                self.SkipList.Remove(id);
            else if (!patch && !self.SkipList.Contains(id))
                self.SkipList.Add(id);
        }


        public static class Flag
        {

            public static bool Get(string k) => Modder.SharedData[k] as bool? ?? false;
            public static void Set(string k, bool v) => Modder.SharedData[k] = v;

        }

        public static class Data
        {

            public static object Get(string k) => Modder.SharedData[k];
            public static void Set(string k, object v) => Modder.SharedData[k] = v;

        }

    }
}
