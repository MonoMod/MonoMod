using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoMod.Utils {
    /// <summary>
    /// Extensions to non-Mono.Cecil types.
    /// </summary>
    public static class Extensions {

        static Type t_ParamArrayAttribute = typeof(ParamArrayAttribute);

        public static string GetFindableID(this System.Reflection.MethodBase method, string name = null, string type = null, bool withType = true, bool proxyMethod = false, bool simple = false) {
            while (method is System.Reflection.MethodInfo && method.IsGenericMethod && !method.IsGenericMethodDefinition)
                method = ((System.Reflection.MethodInfo) method).GetGenericMethodDefinition();

            StringBuilder builder = new StringBuilder();

            if (simple) {
                if (withType)
                    builder.Append(type ?? method.DeclaringType.FullName).Append("::");
                builder.Append(name ?? method.Name);
                return builder.ToString();
            }

            builder
                .Append((method as System.Reflection.MethodInfo)?.ReturnType?.FullName ?? "System.Void")
                .Append(" ");

            if (withType)
                builder.Append(type ?? method.DeclaringType.FullName.Replace("+", "/")).Append("::");

            builder
                .Append(name ?? method.Name);

            if (method.ContainsGenericParameters) {
                builder.Append("<");
                Type[] arguments = method.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++) {
                    if (i > 0)
                        builder.Append(",");
                    builder.Append(arguments[i].Name);
                }
                builder.Append(">");
            }

            builder.Append("(");

            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            for (int i = proxyMethod ? 1 : 0; i < parameters.Length; i++) {
                System.Reflection.ParameterInfo parameter = parameters[i];
                if (i > (proxyMethod ? 1 : 0))
                    builder.Append(",");

                if (Attribute.IsDefined(parameter, t_ParamArrayAttribute))
                    builder.Append("...,");

                builder.Append(parameter.ParameterType.FullName);
            }

            builder.Append(")");

            return builder.ToString();
        }

        public static void AddRange(this IDictionary dict, IDictionary other) {
            foreach (DictionaryEntry entry in other)
                dict.Add(entry.Key, entry.Value);
        }
        public static void AddRange<K, V>(this IDictionary<K, V> dict, IDictionary<K, V> other) {
            foreach (KeyValuePair<K, V> entry in other)
                dict.Add(entry.Key, entry.Value);
        }

        public static void PushRange<T>(this Stack<T> stack, T[] other) {
            foreach (T entry in other)
                stack.Push(entry);
        }
        public static void PopRange<T>(this Stack<T> stack, int n) {
            for (int i = 0; i < n; i++)
                stack.Pop();
        }

        public static void EnqueueRange<T>(this Queue<T> queue, T[] other) {
            foreach (T entry in other)
                queue.Enqueue(entry);
        }
        public static void DequeueRange<T>(this Queue<T> queue, int n) {
            for (int i = 0; i < n; i++)
                queue.Dequeue();
        }

        public static T[] Clone<T>(this T[] array, int length) {
            T[] clone = new T[length];
            Array.Copy(array, clone, length);
            return clone;
        }
        
        public static System.Reflection.MethodInfo FindMethod(this Type type, string findableID, bool simple = true) {
            System.Reflection.MethodInfo[] methods = type.GetMethods(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            );
            // First pass: With type name (f.e. global searches)
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID() == findableID) return method;
            // Second pass: Without type name (f.e. LinkTo)
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID(withType: false) == findableID) return method;

            if (!simple)
                return null;

            // Those shouldn't be reached, unless you're defining a relink map dynamically, which may conflict with itself.
            // First simple pass: With type name (just "Namespace.Type::MethodName")
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID(simple: true) == findableID) return method;
            // Second simple pass: Without type name (basically name only)
            foreach (System.Reflection.MethodInfo method in methods)
                if (method.GetFindableID(withType: false, simple: true) == findableID) return method;

            return null;
        }

    }
}
