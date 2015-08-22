using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoMod constructor attribute.
    /// Apply it onto a constructor and it will be patched by MonoMod.
    /// </summary>
    public class MonoModConstructor : Attribute {
    }
}

