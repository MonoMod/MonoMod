using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod linkto attribute.
    /// Apply it onto a type / method / field and calls to it by mods will be relinked to another target.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModLinkTo : Attribute {
        public MonoModLinkTo(string t) {
        }
        public MonoModLinkTo(Type t) {
        }
        public MonoModLinkTo(string t, string n) {
        }
        public MonoModLinkTo(Type t, string n) {
        }
    }
}

