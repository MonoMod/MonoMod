using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoMod linkto attribute.
    /// Apply it onto a method / field and calls to it by mods will be relinked to another target.
    /// </summary>
    public class MonoModLinkTo : Attribute {
        public MonoModLinkTo(Delegate d) {
        }
        public MonoModLinkTo(Type t, string n) {
        }
        public MonoModLinkTo(string t, string n) {
        }
    }
}

