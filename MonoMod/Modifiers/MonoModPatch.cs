using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod patch attribute.
    /// Apply it onto a type and it will behave as if the type was prefixed with patch_.
    /// This allows for custom compile-time names while MonoMod uses the supplied name for any relinking.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModPatch : Attribute {
        public MonoModPatch(string name) { }
    }
}

