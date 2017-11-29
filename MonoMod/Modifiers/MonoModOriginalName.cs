using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod original name attribute.
    /// Apply it onto a method (not the orig_) and its orig_ method will instead be named like that.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModOriginalName : Attribute {
        public MonoModOriginalName(string n) {
        }
    }
}

