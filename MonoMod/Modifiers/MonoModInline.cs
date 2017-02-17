using System;

public static partial class MMIL {

    public static void DisablePublicAccess() { }
    public static void EnablePublicAccess() { }
    public static void OnPlatform(params Platform[] p) { }

    public class Rule {

        public static void RelinkModule(string i, string o) { }
        public static void RelinkType(string i, string t) { }
        public static void RelinkType(string i, Type t) { }
        public static void RelinkMember(string i, string t, string n) { }
        public static void RelinkMember(string i, Type t, string n) { }

    }

    public class Flag {

        public static bool Get(string k) { return default(bool);  }
        public static void Set(string k, bool v) { }

    }

    public class Data {

        public static object Get(string k) { return null; }
        public static void Set(string k, object v) { }

    }

}
