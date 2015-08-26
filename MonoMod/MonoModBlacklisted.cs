using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoMod blacklist attribute.
    /// Apply it onto a method / type and it will be marked as blacklisted by MonoMod.
    /// </summary>
    public class MonoModBlacklisted : Attribute {
    }
}

