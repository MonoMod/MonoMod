using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.ModInterop {
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ModExportNameAttribute : Attribute {
        public string Name;
        public ModExportNameAttribute(string name) {
            Name = name;
        }
    }
}
