using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.ModInterop {
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public sealed class ModImportNameAttribute : Attribute {
        public string Name;
        public ModImportNameAttribute(string name) {
            Name = name;
        }
    }
}
