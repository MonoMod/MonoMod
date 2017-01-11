using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoMod original name.
    /// Will be applied by MonoMod automatically on original methods. Use it (or MonoModIgnore) to mark non-"orig_" originals!
    /// </summary>
    public class MonoModOriginal : Attribute {
    }
}

