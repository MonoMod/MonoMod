using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod constructor attribute.
    /// Apply it onto a constructor and it will be patched by MonoMod.
    /// Or apply it onto a method and it will be handled like a constructor.
    /// </summary>
    public class MonoModConstructor : Attribute {
    }
}
