using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod target module attribute.
    /// Apply it onto a type and it will only be patched in the target module.
    /// Important: This attribute acts as a filter. It doesn't affect any other module than the input module.
    /// For example, one can define the target assembly version using MonoModTargetModule,
    /// or use the same MonoMod mod on multiple differing input assemblies.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModTargetModule : Attribute {
        public MonoModTargetModule(string name) { }
    }
}

