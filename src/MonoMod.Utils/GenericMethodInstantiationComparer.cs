using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils
{
    public class GenericMethodInstantiationComparer : IEqualityComparer<MethodBase>
    {
        // this may be null on Mono, so we just don't support the magic this does there
        internal static Type? CannonicalFillType = typeof(object).Assembly.GetType("System.__Canon");

        private readonly IEqualityComparer<Type> genericTypeComparer;

        public GenericMethodInstantiationComparer() : this(new GenericTypeInstantiationComparer()) { }
        public GenericMethodInstantiationComparer(IEqualityComparer<Type> typeComparer)
        {
            genericTypeComparer = typeComparer;
        }

        public bool Equals(MethodBase? x, MethodBase? y)
        {
            if (x is null && y is null)
                return true;
            if (x is null || y is null)
                return false;

            var xGeneric = (x.IsGenericMethod && !x.ContainsGenericParameters) || (x.DeclaringType?.IsGenericType ?? false);
            var yGeneric = (y.IsGenericMethod && !y.ContainsGenericParameters) || (y.DeclaringType?.IsGenericType ?? false);
            if (xGeneric != yGeneric)
                return false; // they're clearly different
            if (!xGeneric) // if we get here, they're the same so we only test one
                return x.Equals(y);

            // here we're looking at 2 generic methods
            // so lets start by looking at their declaring types
            if (!genericTypeComparer.Equals(x.DeclaringType!, y.DeclaringType!))
                return false;

            MethodBase xDef;// = xi.GetActualGenericMethodDefinition();
            MethodBase yDef;// = yi.GetActualGenericMethodDefinition();
            if (x is MethodInfo xi)
                xDef = xi.GetActualGenericMethodDefinition();
            else
                xDef = x.GetUnfilledMethodOnGenericType();
            if (y is MethodInfo yi)
                yDef = yi.GetActualGenericMethodDefinition();
            else
                yDef = y.GetUnfilledMethodOnGenericType();

            if (!xDef.Equals(yDef))
                return false;

            if (xDef.Name != yDef.Name)
                return false;

            var xParams = x.GetParameters();
            var yParams = y.GetParameters();

            if (xParams.Length != yParams.Length)
                return false;

            // these should be identical
            var xDefParams = xDef.GetParameters();
            //ParameterInfo[] yDefParams = yDef.GetParameters();

            for (var i = 0; i < xParams.Length; i++)
            {
                var xType = xParams[i].ParameterType;
                var yType = yParams[i].ParameterType;
                if (xDefParams[i].ParameterType.IsGenericParameter)
                {
                    if (!xType.IsValueType)
                    {
                        xType = CannonicalFillType ?? typeof(object); // for some sanity
                    }
                    if (!yType.IsValueType)
                    {
                        yType = CannonicalFillType ?? typeof(object); // for some sanity
                    }
                }
                if (!genericTypeComparer.Equals(xType, yType))
                    return false;
            }

            return true;
        }

        public int GetHashCode(MethodBase obj)
        {
            Helpers.ThrowIfArgumentNull(obj);
            if ((!obj.IsGenericMethod || obj.ContainsGenericParameters) && !(obj.DeclaringType?.IsGenericType ?? false))
                return obj.GetHashCode();

            unchecked
            {
                var code = unchecked((int)0xdeadbeef);
                // ok lets do some magic
                if (obj.DeclaringType != null)
                { // yes, DeclaringType can be null
                    code ^= obj.DeclaringType.Assembly.GetHashCode();
                    code ^= genericTypeComparer.GetHashCode(obj.DeclaringType);
                }
                code ^= obj.Name.GetHashCode(StringComparison.Ordinal);
                var parameters = obj.GetParameters();
                var paramCount = parameters.Length;
                paramCount ^= paramCount << 4;
                paramCount ^= paramCount << 8;
                paramCount ^= paramCount << 16;
                code ^= paramCount;

                if (obj.IsGenericMethod)
                { // we can get here if only the type is generic
                    // type arguments, and here is where we do special treatment
                    var typeArgs = obj.GetGenericArguments();
                    for (var i = 0; i < typeArgs.Length; i++)
                    {
                        var offs = i % 32;
                        var type = typeArgs[i];
                        // this magic is to treat all reference types like System.__Canon, because that's what we care about
                        var typeCode = type.IsValueType ? genericTypeComparer.GetHashCode(type)
                                                        : CannonicalFillType?.GetHashCode() ?? 0x55555555;
                        typeCode = (typeCode << offs) | (typeCode >> (32 - offs)); // this is a rol i believe
                        code ^= typeCode;
                    }
                }

                // parameter types
                MethodBase definition;
                if (obj is MethodInfo info)
                {
                    definition = info.GetActualGenericMethodDefinition();
                }
                else
                {
                    // its probably a constructorinfo or something, so lets use a different method here
                    definition = obj.GetUnfilledMethodOnGenericType();
                }

                var definitionParams = definition.GetParameters();
                // amusingly, this requires the actual definition to behave
                for (var i = 0; i < parameters.Length; i++)
                {
                    var offs = i % 32;
                    var type = parameters[i].ParameterType;
                    var typeCode = genericTypeComparer.GetHashCode(type);
                    // we only normalize when the parameter in question is a generic parameter
                    if (definitionParams[i].ParameterType.IsGenericParameter && !type.IsValueType)
                    {
                        typeCode = CannonicalFillType?.GetHashCode() ?? 0x55555555;
                    }
                    typeCode = (typeCode >> offs) | (typeCode << (32 - offs)); // this is a ror i believe
                    code ^= typeCode;
                }

                return code;
            }
        }
    }
}
