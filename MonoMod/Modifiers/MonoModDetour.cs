using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod detour attribute.
    /// Apply it onto a method and it will be replaced with a managed detour method by MonoMod.
    /// </summary>
    public class MonoModDetour : Attribute {
    }
}

