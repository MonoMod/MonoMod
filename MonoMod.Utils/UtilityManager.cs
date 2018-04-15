using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Utils {
    // This is a horrible place for this.
    public static class UtilityManager {

        private static List<Type> Registered = new List<Type>();

        private static Dictionary<string, List<MethodInfo>> Methods = new Dictionary<string, List<MethodInfo>>();
        private static List<FieldInfo> Fields = new List<FieldInfo>();

        public static void RegisterUtils(this Type type) {
            if (Registered.Contains(type))
                return;
            Registered.Add(type);

            string prefix = type.Assembly.GetName().Name;
            foreach (UtilityAttribute attrib in type.GetCustomAttributes(typeof(UtilityAttribute), false)) {
                prefix = attrib.Name;
            }

            // Collect fields and methods in the type.
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static)) {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType))
                    continue;
                Fields.Add(field);
            }
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                method.RegisterUtil();
                method.RegisterUtil(prefix);
            }

            // Refresh all existing fields and methods.
            foreach (FieldInfo field in Fields) {
                List<MethodInfo> methods;
                if (!Methods.TryGetValue(field.GetUtilName(), out methods)) {
                    field.SetValue(null, null);
                    continue;
                }
                // Set the field to the first matching method, or null.
                bool matched = false;
                foreach (MethodInfo method in methods) {
                    try {
                        field.SetValue(null, Delegate.CreateDelegate(field.FieldType, method));
                        matched = true;
                        break;
                    } catch {
                        // Silently try the next method with the same name.
                    }
                }
                if (!matched)
                    field.SetValue(null, null);
            }
        }

        public static void RegisterUtil(this MethodInfo method, string prefix = null) {
            if (!method.IsPublic || !method.IsStatic)
                throw new MemberAccessException("Utility must be public static");
            string name = method.Name;
            if (!string.IsNullOrEmpty(prefix))
                name = prefix + "." + name;
            List<MethodInfo> methods;
            if (!Methods.TryGetValue(name, out methods))
                Methods[name] = methods = new List<MethodInfo>();
            methods.Add(method);
        }

        private static string GetUtilName(this FieldInfo field) {
            foreach (UtilityAttribute attrib in field.GetCustomAttributes(typeof(UtilityAttribute), false)) {
                return attrib.Name;
            }
            return field.Name;
        }

    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public class UtilityAttribute : Attribute {
        public string Name;
        public UtilityAttribute(string name) {
            Name = name;
        }
    }
}
