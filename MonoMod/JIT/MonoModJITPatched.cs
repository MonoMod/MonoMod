using System;

namespace MonoMod {
    [MonoModIgnore]
    /// <summary>
    /// MonoModJIT patched attribute.
    /// Apply it onto a method / type and it will be marked as patched by MonoModJIT.
    /// Usually only used by MonoModJIT itself.
    /// </summary>
    public class MonoModJITPatched : Attribute {
    }
}

