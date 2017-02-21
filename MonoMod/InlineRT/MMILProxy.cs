using Mono.Cecil;
using StringInject;
using System;
using System.Diagnostics;

namespace MonoMod.InlineRT {
    // TODO automatically create this proxy class
    public static partial class MMILProxy {

        public static class Rule {

            public static void RelinkModule(string from, string toName)
                => MMILRT.Rule.RelinkModule(MMILProxyManager.Self, from, toName);

            public static void RelinkType(string from, string to)
                => MMILRT.Rule.RelinkType(MMILProxyManager.Self, from, to);

            public static void RelinkMember(string from, string toType, string toMember)
                => MMILRT.Rule.RelinkMember(MMILProxyManager.Self, from, toType, toMember);


            public static void Patch(string id, bool patch)
                => MMILRT.Rule.Patch(MMILProxyManager.Self, id, patch);

        }

        public static class Flag {

            public static bool Get(string k)
                => MMILRT.Flag.Get(MMILProxyManager.Self, k);
            public static void Set(string k, bool v)
                => MMILRT.Flag.Set(MMILProxyManager.Self, k, v);

        }

        public static class Data {

            public static object Get(string k)
                => MMILRT.Data.Get(MMILProxyManager.Self, k);
            public static void Set(string k, object v)
                => MMILRT.Data.Set(MMILProxyManager.Self, k, v);

        }

    }
}
