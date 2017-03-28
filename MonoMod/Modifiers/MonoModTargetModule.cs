using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod target mpdule attribute.
    /// Apply it onto a type and it will only be patched in the target module.
    /// This allows for one MonoMod mod to be used on multiple assemblies with differing specifics.
    /// It doesn't affect any other module than the input module, though.
    /// </summary>
    public class MonoModTargetModule : Attribute {
        public MonoModTargetModule(string name) { }
    }
}

