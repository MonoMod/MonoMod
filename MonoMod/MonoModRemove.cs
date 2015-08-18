using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoMod remove attribute.
    /// Apply it onto a method / type and it will be removed by MonoMod.
    /// </summary>
    public class MonoModRemove : Attribute {
    }
}

