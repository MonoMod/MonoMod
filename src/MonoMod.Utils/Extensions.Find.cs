﻿using Mono.Cecil;
using System;
using System.Reflection;

namespace MonoMod.Utils
{
    public static partial class Extensions
    {

        /// <summary>
        /// Find a method for a given ID.
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="id">The method ID.</param>
        /// <param name="simple">Whether to perform a simple search pass as well or not.</param>
        /// <returns>The first matching method or null.</returns>
        public static MethodDefinition? FindMethod(this TypeDefinition type, string id, bool simple = true)
        {
            Helpers.ThrowIfArgumentNull(type);
            Helpers.ThrowIfArgumentNull(id);
            if (simple && !id.Contains(' ', StringComparison.Ordinal))
            {
                // First simple pass: With type name (just "Namespace.Type::MethodName")
                foreach (var method in type.Methods)
                    if (method.GetID(simple: true) == id)
                        return method;
                // Second simple pass: Without type name (basically name only)
                foreach (var method in type.Methods)
                    if (method.GetID(withType: false, simple: true) == id)
                        return method;
            }

            // First pass: With type name (f.e. global searches)
            foreach (var method in type.Methods)
                if (method.GetID() == id)
                    return method;
            // Second pass: Without type name (f.e. LinkTo)
            foreach (var method in type.Methods)
                if (method.GetID(withType: false) == id)
                    return method;

            return null;
        }
        /// <summary>
        /// Find a method for a given ID recursively (including the passed type's base types).
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="id">The method ID.</param>
        /// <param name="simple">Whether to perform a simple search pass as well or not.</param>
        /// <returns>The first matching method or null.</returns>
        public static MethodDefinition? FindMethodDeep(this TypeDefinition type, string id, bool simple = true)
        {
            return Helpers.ThrowIfNull(type).FindMethod(id, simple) ?? type.BaseType?.Resolve()?.FindMethodDeep(id, simple);
        }

        /// <summary>
        /// Find a method for a given ID.
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="id">The method ID.</param>
        /// <param name="simple">Whether to perform a simple search pass as well or not.</param>
        /// <returns>The first matching method or null.</returns>
        public static MethodInfo? FindMethod(this Type type, string id, bool simple = true)
        {
            Helpers.ThrowIfArgumentNull(type);
            Helpers.ThrowIfArgumentNull(id);

            var methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic
            );

            if (simple && !id.Contains(' ', StringComparison.Ordinal))
            {
                // First simple pass: With type name (just "Namespace.Type::MethodName")
                foreach (var method in methods)
                    if (method.GetID(simple: true) == id)
                        return method;
                // Second simple pass: Without type name (basically name only)
                foreach (var method in methods)
                    if (method.GetID(withType: false, simple: true) == id)
                        return method;
            }

            // First pass: With type name (f.e. global searches)
            foreach (var method in methods)
                if (method.GetID() == id)
                    return method;
            // Second pass: Without type name (f.e. LinkTo)
            foreach (var method in methods)
                if (method.GetID(withType: false) == id)
                    return method;

            return null;
        }
        /// <summary>
        /// Find a method for a given ID recursively (including the passed type's base types).
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="id">The method ID.</param>
        /// <param name="simple">Whether to perform a simple search pass as well or not.</param>
        /// <returns>The first matching method or null.</returns>
        public static MethodInfo? FindMethodDeep(this Type type, string id, bool simple = true)
        {
            return type.FindMethod(id, simple) ?? type.BaseType?.FindMethodDeep(id, simple);
        }

        /// <summary>
        /// Find a property for a given name.
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The first matching property or null.</returns>
        public static PropertyDefinition? FindProperty(this TypeDefinition type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            foreach (var prop in type.Properties)
                if (prop.Name == name)
                    return prop;
            return null;
        }
        /// <summary>
        /// Find a property for a given name recursively (including the passed type's base types).
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The first matching property or null.</returns>
        public static PropertyDefinition? FindPropertyDeep(this TypeDefinition type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            return type.FindProperty(name) ?? type.BaseType?.Resolve()?.FindPropertyDeep(name);
        }

        /// <summary>
        /// Find a field for a given name.
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="name">The field name.</param>
        /// <returns>The first matching field or null.</returns>
        public static FieldDefinition? FindField(this TypeDefinition type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            foreach (var field in type.Fields)
                if (field.Name == name)
                    return field;
            return null;
        }
        /// <summary>
        /// Find a field for a given name recursively (including the passed type's base types).
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="name">The field name.</param>
        /// <returns>The first matching field or null.</returns>
        public static FieldDefinition? FindFieldDeep(this TypeDefinition type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            return type.FindField(name) ?? type.BaseType?.Resolve()?.FindFieldDeep(name);
        }

        /// <summary>
        /// Find an event for a given name.
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="name">The event name.</param>
        /// <returns>The first matching event or null.</returns>
        public static EventDefinition? FindEvent(this TypeDefinition type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            foreach (var eventDef in type.Events)
                if (eventDef.Name == name)
                    return eventDef;
            return null;
        }
        /// <summary>
        /// Find an event for a given name recursively (including the passed type's base types).
        /// </summary>
        /// <param name="type">The type to search in.</param>
        /// <param name="name">The event name.</param>
        /// <returns>The first matching event or null.</returns>
        public static EventDefinition? FindEventDeep(this TypeDefinition type, string name)
        {
            Helpers.ThrowIfArgumentNull(type);
            return type.FindEvent(name) ?? type.BaseType?.Resolve()?.FindEventDeep(name);
        }

    }
}
