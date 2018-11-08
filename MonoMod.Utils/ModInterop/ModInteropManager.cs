using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.ModInterop {
    public static class ModInteropManager {

        private static HashSet<Type> Registered = new HashSet<Type>();

        private static Dictionary<string, List<MethodInfo>> Methods = new Dictionary<string, List<MethodInfo>>();
        private static List<FieldInfo> Fields = new List<FieldInfo>();

        public static void ModInterop(this Type type) {
            if (Registered.Contains(type))
                return;
            Registered.Add(type);

            string prefix = type.GetTypeInfo().Assembly.GetName().Name;
            foreach (ModExportNameAttribute attrib in type.GetTypeInfo().GetCustomAttributes(typeof(ModExportNameAttribute), false)) {
                prefix = attrib.Name;
            }

            // Collect fields and methods in the type.
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static)) {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType))
                    continue;
                Fields.Add(field);
            }
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                method.RegisterModExport();
                method.RegisterModExport(prefix);
            }

            // Refresh all existing fields and methods.
            foreach (FieldInfo field in Fields) {
                List<MethodInfo> methods;
                if (!Methods.TryGetValue(field.GetModImportName(), out methods)) {
                    field.SetValue(null, null);
                    continue;
                }
                // Set the field to the first matching method, or null.
                bool matched = false;
                foreach (MethodInfo method in methods) {
                    try {
                        field.SetValue(null, NETStandardShims.CreateDelegate(field.FieldType, null, method));
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

        public static void RegisterModExport(this MethodInfo method, string prefix = null) {
            if (!method.IsPublic || !method.IsStatic)
                throw new MemberAccessException("Utility must be public static");
            string name = method.Name;
            if (!string.IsNullOrEmpty(prefix))
                name = prefix + "." + name;

            List<MethodInfo> methods;
            if (!Methods.TryGetValue(name, out methods))
                Methods[name] = methods = new List<MethodInfo>();

            if (!methods.Contains(method))
                methods.Add(method);
        }

        private static string GetModImportName(this FieldInfo field) {
            foreach (ModImportNameAttribute attrib in field.GetCustomAttributes(typeof(ModImportNameAttribute), false)) {
                return attrib.Name;
            }

            foreach (ModImportNameAttribute attrib in field.DeclaringType.GetTypeInfo().GetCustomAttributes(typeof(ModImportNameAttribute), false)) {
                return attrib.Name + "." + field.Name;
            }

            return field.Name;
        }

    }
}
