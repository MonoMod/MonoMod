using MonoMod;
using System;

public static partial class MMIL {

    public static class Access {

        public static object Call<TSelf>(TSelf self, string name, params object[] args) { return null; }
        public static object CallT(object self, string type, string name, params object[] args) { return null; }

        public static object Get<TSelf>(TSelf self, string name) { return null; }
        public static object GetT(object self, string type, string name) { return null; }

        public static void Set<TValue>(string type, string name, TValue value) { }
        public static void SetT<TValue>(object self, string type, string name, TValue value) { }

    }

}

namespace MMILExt {
    public static partial class MMILExt {

        [MonoModLinkTo("MMIL/Access", "Call")]
        public static object MMILCall<TSelf>(this TSelf self, string name, params object[] args) { return null; }
        [MonoModLinkTo("MMIL/Access", "CallT")]
        public static object MMILCallT(this object self, string type, string name, params object[] args) { return null; }

        [MonoModLinkTo("MMIL/Access", "GetT")]
        public static object MMILGet<TSelf>(this TSelf self, string name) { return null; }
        [MonoModLinkTo("MMIL/Access", "Get")]
        public static object MMILGetT(this object self, string type, string name) { return null; }

        [MonoModLinkTo("MMIL/Access", "Set")]
        public static void MMILSet<TSelf, TValue>(this TSelf self, string name, TValue value) { }
        [MonoModLinkTo("MMIL/Access", "SetT")]
        public static void MMILSetT<TValue>(this object self, string type, string name, TValue value) { }

    }
}
