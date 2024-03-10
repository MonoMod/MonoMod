using System;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Mono.Cecil;

namespace MonoMod
{
    public static partial class PatcherExtensions
    {

        public static void SetPublic(this IMetadataTokenProvider mtp, bool p)
        {
            if (mtp is TypeDefinition)
                ((TypeDefinition)mtp).SetPublic(p);
            else if (mtp is FieldDefinition)
                ((FieldDefinition)mtp).SetPublic(p);
            else if (mtp is MethodDefinition)
                ((MethodDefinition)mtp).SetPublic(p);
            else if (mtp is PropertyDefinition)
                ((PropertyDefinition)mtp).SetPublic(p);
            else if (mtp is EventDefinition)
                ((EventDefinition)mtp).SetPublic(p);
            else
                throw new InvalidOperationException($"MonoMod can't set metadata token providers of the type {mtp.GetType()} public.");
        }
        public static void SetPublic(this FieldDefinition o, bool p)
        {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.IsPrivate = !p;
            o.IsPublic = p;
            if (p)
                o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this MethodDefinition o, bool p)
        {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.IsPrivate = !p;
            o.IsPublic = p;
            if (p)
                o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this PropertyDefinition o, bool p)
        {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.GetMethod?.SetPublic(p);
            o.SetMethod?.SetPublic(p);
            foreach (MethodDefinition method in o.OtherMethods)
                method.SetPublic(p);
            if (p)
                o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this EventDefinition o, bool p)
        {
            if (!o.IsDefinition || o.DeclaringType.Name == "<PrivateImplementationDetails>")
                return;
            o.AddMethod?.SetPublic(p);
            o.RemoveMethod?.SetPublic(p);
            o.InvokeMethod?.SetPublic(p);
            foreach (MethodDefinition method in o.OtherMethods)
                method.SetPublic(p);
            if (p)
                o.DeclaringType.SetPublic(true);
        }
        public static void SetPublic(this TypeDefinition o, bool p)
        {
            if (
                !o.IsDefinition ||
                o.Name == "<PrivateImplementationDetails>" ||
                (o.DeclaringType != null && o.DeclaringType.Name == "<PrivateImplementationDetails>")
            )
                return;
            if (o.DeclaringType == null)
            {
                o.IsNotPublic = !p;
                o.IsPublic = p;
            }
            else
            {
                o.IsNestedPrivate = !p;
                o.IsNestedPublic = p;
                if (p)
                    SetPublic(o.DeclaringType, true);
            }
        }

        // Required for field -> call conversions where the original access was an address access.
        internal static void AppendGetAddr(this MethodBody body, Instruction instr, TypeReference type, IDictionary<TypeReference, VariableDefinition> localMap = null)
        {
            if (localMap == null || !localMap.TryGetValue(type, out VariableDefinition local))
            {
                local = new VariableDefinition(type);
                body.Variables.Add(local);
                if (localMap != null)
                    localMap[type] = local;
            }

            ILProcessor il = body.GetILProcessor();
            Instruction tmp = instr;
            il.InsertAfter(tmp, tmp = il.Create(OpCodes.Stloc, local));
            il.InsertAfter(tmp, tmp = il.Create(OpCodes.Ldloca, local));
        }

        internal static CustomAttribute GetNextCustomAttribute(this ICustomAttributeProvider cap, string attribute)
        {
            if (cap == null || !cap.HasCustomAttributes)
                return null;
            var next = false;
            for (var i = 0; i < cap.CustomAttributes.Count; i++)
            {
                CustomAttribute attrib = cap.CustomAttributes[i];
                if (attrib.AttributeType.FullName != attribute)
                    continue;
                if (!next)
                {
                    cap.CustomAttributes.RemoveAt(i);
                    i--;
                    next = true;
                    continue;
                }
                return attrib;
            }
            return null;
        }

        public static string GetOriginalName(this MethodDefinition method)
        {
            foreach (CustomAttribute attrib in method.CustomAttributes)
                if (attrib.AttributeType.FullName == "MonoMod.MonoModOriginalName")
                    return (string)attrib.ConstructorArguments[0].Value;

            if (method.Name == ".ctor" || method.Name == ".cctor")
            {
                return "orig_ctor_" + ((MemberReference)method.DeclaringType).GetPatchName();
            }

            return "orig_" + method.Name;
        }

    }
}
