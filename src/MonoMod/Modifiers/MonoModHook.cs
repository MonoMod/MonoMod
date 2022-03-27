using System;

namespace MonoMod {
    /// <summary>
    /// MonoMod hook attribute.
    /// Apply it onto a type / method / field and calls to the item it hooks will be relinked to the item the attribute gets applied to.
    /// </summary>
    [MonoMod__SafeToCopy__]
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    [Obsolete("Use MonoModLinkFrom or RuntimeDetour / HookGen instead.")]
    public class MonoModHook : Attribute {
        public string FindableID;
        public Type Type;
        public MonoModHook(string findableID) {
            FindableID = findableID;
        }
        public MonoModHook(Type type) {
            Type = type;
            FindableID = type.FullName;
        }
    }
}
