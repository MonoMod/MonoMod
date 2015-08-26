using System;

namespace MonoMod {
    public class BlacklistItem {

        public string AssemblyName;
        public string FullName;

        public BlacklistItem(string assemblyName = null, string fullName = null) {
            AssemblyName = assemblyName;
            FullName = fullName;
        }

    }
}

