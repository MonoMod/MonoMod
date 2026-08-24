using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Linq;
using System.Reflection;

namespace MonoMod.RuntimeDetour.Generics
{
    public static class GenericHelper
    {
        internal static bool IsContextForwarded(GenericContextReader.GenericContextKind kind)
            => kind != GenericContextReader.GenericContextKind.ThisObject && PlatformDetection.Runtime != RuntimeKind.Mono;

        internal static bool HasReturnBuffer(Type returnType) 
            => returnType != typeof(void) && PlatformTriple.Current.Abi.Classify(returnType, true) is TypeClassification.ByReference;

        /// <summary>True if this concrete type participates in reference-type code sharing.</summary>
        public static bool TypeIsShared(Type type)
        {
            Helpers.ThrowIfArgumentNull(type);
            if (type.ContainsGenericParameters)
            {
                throw new InvalidOperationException("Cannot classify an open generic type.");
            }

            if (type.IsPrimitive)
            {
                return false;
            }

            if (type.IsValueType)
            {
                return type.IsGenericType && type.GetGenericArguments().Any(TypeIsShared);
            }

            return true;
        }

        /// <summary>
        /// True if the concrete method routes through shared code (i.e. its real type
        /// arguments are only recoverable from a hidden generic context at runtime).
        /// </summary>
        public static bool MethodIsShared(MethodBase method)
        {
            Helpers.ThrowIfArgumentNull(method);
            var dt = method.DeclaringType;
            if (dt is { IsGenericType: true } && !dt.ContainsGenericParameters && dt.GetGenericArguments().Any(TypeIsShared))
            {
                return true;
            }

            if (method.IsGenericMethod && !method.ContainsGenericParameters && method.GetGenericArguments().Any(TypeIsShared))
            {
                return true;
            }

            return false;
        }
    }
}
