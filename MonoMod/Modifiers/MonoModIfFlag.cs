using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod "if" attribute.
    /// Apply it onto a type / method / field and it gets ignored if MonoModder.Data[key] equals false.
    /// If fallback (true by default) is false, it also gets ignored if MonoModder.Data[key] is undefined.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModIfFlag : Attribute {
        public MonoModIfFlag(string key) {
        }
        public MonoModIfFlag(string key, bool fallback) {
        }
    }
}
