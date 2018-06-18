using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod "static hook" attribute.
    /// Apply it onto a type / method / field and calls to the item it hooks will be relinked to the item the attribute gets applied to.
    /// </summary>
    [MonoMod__SafeToCopy__]
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class MonoModLinkFrom : Attribute {
        public string FindableID;
        public Type Type;
        public MonoModLinkFrom(string findableID) {
            FindableID = findableID;
        }
        public MonoModLinkFrom(Type type) {
            Type = type;
            FindableID = type.FullName;
        }
    }
}
