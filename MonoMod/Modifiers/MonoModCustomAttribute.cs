using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod "custom attribute" attribute.
    /// Apply it onto a custom attribute type and the supplied handler in your MonoModRules will handle it.
    /// Replaces MMIL.Rule.RegisterCustomAttribute in MonoModRules constructor.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModCustomAttributeAttribute : Attribute {
        public MonoModCustomAttributeAttribute(string h) {
        }
    }

    /// <summary>
    /// MonoMod "custom method attribute" attribute.
    /// Apply it onto a custom attribute type and the supplied handler in your MonoModRules will handle it.
    /// Replaces MMIL.Rule.RegisterCustomMethodAttribute in MonoModRules constructor.
    /// </summary>
    [MonoMod__SafeToCopy__]
    public class MonoModCustomMethodAttributeAttribute : Attribute {
        public MonoModCustomMethodAttributeAttribute(string h) {
        }
    }
}
