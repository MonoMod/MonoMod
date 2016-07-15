using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoMod "only patch if it exists" attribute.
    /// Apply it onto a type or method and it gets ignored if there's no original method in the input module.
    /// </summary>
    public class MonoModNoNew : Attribute {
        public MonoModNoNew() {
        }
    }
}

