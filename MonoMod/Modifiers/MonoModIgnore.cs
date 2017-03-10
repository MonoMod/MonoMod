using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod ignore attribute.
    /// Apply it onto a method / type and it (except its MonoMod custom attributes) will be ignored by MonoMod.
    /// </summary>
    public class MonoModIgnore : Attribute {
    }
}

