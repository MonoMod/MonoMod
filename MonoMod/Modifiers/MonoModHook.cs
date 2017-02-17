using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod hook attribute.
    /// Apply it onto a type / method / field and calls to the item it hooks will be relinked to the item the attribute gets applied to.
    /// </summary>
    public class MonoModHook : Attribute {
        public MonoModHook(string f) {
        }
        public MonoModHook(Type f) {
        }
    }
}
