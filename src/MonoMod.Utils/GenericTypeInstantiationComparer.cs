using System;
using System.Collections.Generic;

namespace MonoMod.Utils
{
    public class GenericTypeInstantiationComparer : IEqualityComparer<Type>
    {
        private static Type? CannonicalFillType = GenericMethodInstantiationComparer.CannonicalFillType;

        public bool Equals(Type? x, Type? y)
        {
            if (x is null && y is null)
                return true;
            if (x is null || y is null)
                return false;

            var xGeneric = x.IsGenericType;
            var yGeneric = y.IsGenericType;
            if (xGeneric != yGeneric)
                return false;
            if (!xGeneric)
                return x.Equals(y);

            // both are generic
            var xDef = x.GetGenericTypeDefinition();
            var yDef = y.GetGenericTypeDefinition();
            if (!xDef.Equals(yDef))
                return false; // definitions aren't equal, so we aren't equal

            var xGenArgs = x.GetGenericArguments();
            var yGenArgs = y.GetGenericArguments();
            if (xGenArgs.Length != yGenArgs.Length)
                return false;

            for (var i = 0; i < xGenArgs.Length; i++)
            {
                var xArg = xGenArgs[i];
                var yArg = yGenArgs[i];

                if (!xArg.IsValueType)
                    xArg = CannonicalFillType ?? typeof(object);
                if (!yArg.IsValueType)
                    yArg = CannonicalFillType ?? typeof(object);

                if (!Equals(xArg, yArg))
                    return false;
            }

            return true;
        }

        public int GetHashCode(Type obj)
        {
            Helpers.ThrowIfArgumentNull(obj);
            if (!obj.IsGenericType)
                return obj.GetHashCode();

            // TODO: use HashCode
            unchecked
            {
                var code = unchecked((int)0xdeadbeef);
                code ^= obj.Assembly.GetHashCode();
                code ^= (code << 16) | (code >> 16);

                if (obj.Namespace != null)
                    code ^= obj.Namespace.GetHashCode(StringComparison.Ordinal);

                code ^= obj.Name.GetHashCode(StringComparison.Ordinal);

                var genericParams = obj.GetGenericArguments();

                for (var i = 0; i < genericParams.Length; i++)
                {
                    var offs = i % 8 * 4;
                    var param = genericParams[i];
                    var typeCode = param.IsValueType ? GetHashCode(param)
                                                     : CannonicalFillType?.GetHashCode() ?? (int)0x99999999;
                    code ^= (typeCode << offs) | (typeCode >> (32 - offs));
                }

                return code;
            }
        }
    }
}
