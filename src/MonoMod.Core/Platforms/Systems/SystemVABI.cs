using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Core.Platforms.Systems
{
    internal static class SystemVABI
    {

        private static readonly ConditionalWeakTable<Type, StrongBox<bool>> SysVIsMemoryCache = new();

        public static TypeClassification ClassifyAMD64(Type type, bool isReturn)
        {
            var totalSize = type.GetManagedSize();
            if (totalSize > 16)
            {
                if (totalSize > 32)
                    return isReturn ? TypeClassification.ByReference : TypeClassification.OnStack;

                var isMemory = SysVIsMemoryCache.GetValue(
                    type,
                    static t => new StrongBox<bool>(AnyFieldsNotFloat(t))
                ).Value;
                if (isMemory)
                {
                    return isReturn ? TypeClassification.ByReference : TypeClassification.OnStack;
                }
            }
            return TypeClassification.InRegister;
        }

        private static bool AnyFieldsNotFloat(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var fieldType = field.FieldType;
                if (fieldType is { IsPrimitive: false, IsValueType: true } && AnyFieldsNotFloat(fieldType))
                    return true;
                if (Type.GetTypeCode(fieldType) is not TypeCode.Single and not TypeCode.Double)
                    return true;
            }

            return false;
        }

    }
}
