using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod original method attribute.
    /// Will be applied by MonoMod automatically on original methods.
    /// Use this (or MonoModIgnore) manually to mark non-"orig_" originals!
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModOriginal : Attribute {
    }
}

