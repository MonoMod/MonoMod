using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Utils {
    public class GenericMethodInstantiationComparer : IEqualityComparer<MethodBase> {
        // this may be null on Mono, so we just don't support the magic this does there
        internal static Type CannonicalFillType = typeof(object).Assembly.GetType("System.__Canon");

        private readonly IEqualityComparer<Type> genericTypeComparer;

        public GenericMethodInstantiationComparer() : this(new GenericTypeInstantiationComparer()) { }
        public GenericMethodInstantiationComparer(IEqualityComparer<Type> typeComparer) {
            genericTypeComparer = typeComparer;
        }

        public bool Equals(MethodBase x, MethodBase y) {
            if (x is null && y is null)
                return true;
            if (x is null || y is null)
                return false;

            bool xGeneric = (x.IsGenericMethod && !x.ContainsGenericParameters) || (x.DeclaringType?.IsGenericType ?? false);
            bool yGeneric = (y.IsGenericMethod && !y.ContainsGenericParameters) || (y.DeclaringType?.IsGenericType ?? false);
            if (xGeneric != yGeneric)
                return false; // they're clearly different
            if (!xGeneric) // if we get here, they're the same so we only test one
                return x.Equals(y);

            // here we're looking at 2 generic methods
            // so lets start by looking at their declaring types
            if (!genericTypeComparer.Equals(x.DeclaringType, y.DeclaringType))
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

            ParameterInfo[] xParams = x.GetParameters();
            ParameterInfo[] yParams = y.GetParameters();

            if (xParams.Length != yParams.Length)
                return false;

            // these should be identical
            ParameterInfo[] xDefParams = xDef.GetParameters();
            ParameterInfo[] yDefParams = yDef.GetParameters();

            for (int i = 0; i < xParams.Length; i++) {
                Type xType = xParams[i].ParameterType;
                Type yType = yParams[i].ParameterType;
                if (xDefParams[i].ParameterType.IsGenericParameter) {
                    if (!xType.IsValueType) {
                        xType = CannonicalFillType ?? typeof(object); // for some sanity
                    }
                    if (!yType.IsValueType) {
                        yType = CannonicalFillType ?? typeof(object); // for some sanity
                    }
                }
                if (!genericTypeComparer.Equals(xType, yType))
                    return false;
            }

            return true;
        }

        public int GetHashCode(MethodBase method) {
            if ((!method.IsGenericMethod || method.ContainsGenericParameters) && !(method.DeclaringType?.IsGenericType ?? false))
                return method.GetHashCode();

            unchecked {
                int code = unchecked((int) 0xdeadbeef);
                // ok lets do some magic
                if (method.DeclaringType != null) { // yes, DeclaringType can be null
                    code ^= method.DeclaringType.Assembly.GetHashCode();
                    code ^= genericTypeComparer.GetHashCode(method.DeclaringType);
                }
                code ^= method.Name.GetHashCode();
                ParameterInfo[] parameters = method.GetParameters();
                int paramCount = parameters.Length;
                paramCount ^= paramCount << 4;
                paramCount ^= paramCount << 8;
                paramCount ^= paramCount << 16;
                code ^= paramCount;

                if (method.IsGenericMethod) { // we can get here if only the type is generic
                    // type arguments, and here is where we do special treatment
                    Type[] typeArgs = method.GetGenericArguments();
                    for (int i = 0; i < typeArgs.Length; i++) {
                        int offs = i % 32;
                        Type type = typeArgs[i];
                        // this magic is to treat all reference types like System.__Canon, because that's what we care about
                        int typeCode = type.IsValueType ? genericTypeComparer.GetHashCode(type)
                                                        : CannonicalFillType?.GetHashCode() ?? 0x55555555;
                        typeCode = (typeCode << offs) | (typeCode >> (32 - offs)); // this is a rol i believe
                        code ^= typeCode;
                    }
                }

                // parameter types
                MethodBase definition;
                if (method is MethodInfo info) {
                    definition = info.GetActualGenericMethodDefinition();
                } else {
                    // its probably a constructorinfo or something, so lets use a different method here
                    definition = method.GetUnfilledMethodOnGenericType();
                }

                ParameterInfo[] definitionParams = definition.GetParameters();
                // amusingly, this requires the actual definition to behave
                for (int i = 0; i < parameters.Length; i++) {
                    int offs = i % 32;
                    Type type = parameters[i].ParameterType;
                    int typeCode = genericTypeComparer.GetHashCode(type);
                    // we only normalize when the parameter in question is a generic parameter
                    if (definitionParams[i].ParameterType.IsGenericParameter && !type.IsValueType) {
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
