using MonoMod.Utils;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace MonoMod.UnitTest
{
    public class GenericComparisonTests
    {

        [Theory]
        [InlineData(typeof(string), typeof(string))]
        [InlineData(typeof(object), typeof(object))]
        [InlineData(typeof(int), typeof(int))]
        [InlineData(typeof(int?), typeof(int?))]
        [InlineData(typeof(GenericA<>), typeof(GenericA<>))]
        [InlineData(typeof(GenericA<object>), typeof(GenericA<object>))]
        [InlineData(typeof(GenericA<string>), typeof(GenericA<string>))]
        [InlineData(typeof(GenericA<int>), typeof(GenericA<int>))]
        [InlineData(typeof(GenericA<int?>), typeof(GenericA<int?>))]
        [InlineData(typeof(GenericA<object>), typeof(GenericA<string>))]
        [InlineData(typeof(GenericA<string>), typeof(GenericA<object>))]
        [InlineData(typeof(ValueGenericA<>), typeof(ValueGenericA<>))]
        [InlineData(typeof(ValueGenericA<object>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(ValueGenericA<string>), typeof(ValueGenericA<string>))]
        [InlineData(typeof(ValueGenericA<int>), typeof(ValueGenericA<int>))]
        [InlineData(typeof(ValueGenericA<int?>), typeof(ValueGenericA<int?>))]
        [InlineData(typeof(ValueGenericA<object>), typeof(ValueGenericA<string>))]
        [InlineData(typeof(ValueGenericA<string>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(GenericA<ValueGenericA<object>>), typeof(GenericA<ValueGenericA<object>>))]
        [InlineData(typeof(GenericA<ValueGenericA<string>>), typeof(GenericA<ValueGenericA<string>>))]
        [InlineData(typeof(GenericA<ValueGenericA<int>>), typeof(GenericA<ValueGenericA<int>>))]
        [InlineData(typeof(GenericA<ValueGenericA<int?>>), typeof(GenericA<ValueGenericA<int?>>))]
        [InlineData(typeof(GenericA<ValueGenericA<object>>), typeof(GenericA<ValueGenericA<string>>))]
        [InlineData(typeof(GenericA<ValueGenericA<string>>), typeof(GenericA<ValueGenericA<object>>))]
        [InlineData(typeof(ValueGenericA<GenericA<object>>), typeof(ValueGenericA<GenericA<object>>))]
        [InlineData(typeof(ValueGenericA<GenericA<string>>), typeof(ValueGenericA<GenericA<string>>))]
        [InlineData(typeof(ValueGenericA<GenericA<int>>), typeof(ValueGenericA<GenericA<int>>))]
        [InlineData(typeof(ValueGenericA<GenericA<int?>>), typeof(ValueGenericA<GenericA<int?>>))]
        [InlineData(typeof(ValueGenericA<GenericA<object>>), typeof(ValueGenericA<GenericA<string>>))]
        [InlineData(typeof(ValueGenericA<GenericA<string>>), typeof(ValueGenericA<GenericA<object>>))]
        [InlineData(typeof(ValueGenericA<GenericA<object>>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(ValueGenericA<GenericA<string>>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(ValueGenericA<GenericA<int>>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(ValueGenericA<GenericA<int?>>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(ValueGenericA<GenericA<object>>), typeof(ValueGenericA<GenericA<int>>))]
        [InlineData(typeof(ValueGenericA<GenericA<string>>), typeof(ValueGenericA<GenericA<int>>))]
        [InlineData(typeof(ValueGenericA<GenericA<int>>), typeof(ValueGenericA<GenericA<object>>))]
        [InlineData(typeof(ValueGenericA<GenericA<int>>), typeof(ValueGenericA<GenericA<string>>))]
        [InlineData(typeof(ValueGenericA<GenericA<int>>), typeof(ValueGenericA<GenericA<int?>>))]
        [InlineData(typeof(ValueGenericA<GenericA<int?>>), typeof(ValueGenericA<GenericA<int>>))]
        public void TypesCompareEqual(Type a, Type b)
        {
            var comparer = new GenericTypeInstantiationComparer();
            Assert.True(comparer.Equals(a, b));
            Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
        }

        [Theory]
        [InlineData(typeof(string), typeof(object))]
        [InlineData(typeof(object), typeof(string))]
        [InlineData(typeof(int), typeof(int?))]
        [InlineData(typeof(int?), typeof(int))]
        [InlineData(typeof(GenericA<>), typeof(ValueGenericA<>))]
        [InlineData(typeof(ValueGenericA<>), typeof(GenericA<>))]
        [InlineData(typeof(GenericA<object>), typeof(GenericA<int>))]
        [InlineData(typeof(GenericA<string>), typeof(GenericA<int>))]
        [InlineData(typeof(GenericA<int>), typeof(GenericA<object>))]
        [InlineData(typeof(GenericA<int>), typeof(GenericA<string>))]
        [InlineData(typeof(GenericA<int>), typeof(GenericA<int?>))]
        [InlineData(typeof(GenericA<int?>), typeof(GenericA<int>))]
        [InlineData(typeof(ValueGenericA<object>), typeof(ValueGenericA<int>))]
        [InlineData(typeof(ValueGenericA<string>), typeof(ValueGenericA<int>))]
        [InlineData(typeof(ValueGenericA<int>), typeof(ValueGenericA<object>))]
        [InlineData(typeof(ValueGenericA<int>), typeof(ValueGenericA<string>))]
        [InlineData(typeof(ValueGenericA<int>), typeof(ValueGenericA<int?>))]
        [InlineData(typeof(ValueGenericA<int?>), typeof(ValueGenericA<int>))]
        [InlineData(typeof(GenericA<ValueGenericA<object>>), typeof(GenericA<ValueGenericA<int>>))]
        [InlineData(typeof(GenericA<ValueGenericA<string>>), typeof(GenericA<ValueGenericA<int>>))]
        [InlineData(typeof(GenericA<ValueGenericA<int>>), typeof(GenericA<ValueGenericA<object>>))]
        [InlineData(typeof(GenericA<ValueGenericA<int>>), typeof(GenericA<ValueGenericA<string>>))]
        [InlineData(typeof(GenericA<ValueGenericA<int>>), typeof(GenericA<ValueGenericA<int?>>))]
        [InlineData(typeof(GenericA<ValueGenericA<int?>>), typeof(GenericA<ValueGenericA<int>>))]
        public void TypesCompareInequal(Type a, Type b)
        {
            var comparer = new GenericTypeInstantiationComparer();
            Assert.False(comparer.Equals(a, b));
        }

        [Theory]
        [InlineData(typeof(object), nameof(Object.ToString), null, typeof(object), nameof(Object.ToString), null)]
        [InlineData(typeof(string), nameof(string.ToString), null, typeof(string), nameof(string.ToString), null)]
        [InlineData(typeof(int), nameof(int.TryParse), null, typeof(int), nameof(int.TryParse), null)]
        [InlineData(typeof(int?), nameof(Nullable<int>.GetValueOrDefault), null, typeof(int?), nameof(Nullable<int>.GetValueOrDefault), null)]
        [InlineData(typeof(GenericA<object>), "NG", null, typeof(GenericA<object>), "NG", null)]
        [InlineData(typeof(GenericA<string>), "NG", null, typeof(GenericA<string>), "NG", null)]
        [InlineData(typeof(GenericA<object>), "NG", null, typeof(GenericA<string>), "NG", null)]
        [InlineData(typeof(GenericA<string>), "NG", null, typeof(GenericA<object>), "NG", null)]
        [InlineData(typeof(GenericA<int>), "NG", null, typeof(GenericA<int>), "NG", null)]
        [InlineData(typeof(GenericA<int?>), "NG", null, typeof(GenericA<int?>), "NG", null)]
        [InlineData(typeof(GenericA<object>), "G", null, typeof(GenericA<object>), "G", null)]
        [InlineData(typeof(GenericA<string>), "G", null, typeof(GenericA<string>), "G", null)]
        [InlineData(typeof(GenericA<object>), "G", null, typeof(GenericA<string>), "G", null)]
        [InlineData(typeof(GenericA<string>), "G", null, typeof(GenericA<object>), "G", null)]
        [InlineData(typeof(GenericA<int>), "G", null, typeof(GenericA<int>), "G", null)]
        [InlineData(typeof(GenericA<int?>), "G", null, typeof(GenericA<int?>), "G", null)]
        [InlineData(typeof(GenericA<object>), "G", typeof(object), typeof(GenericA<object>), "G", typeof(object))]
        [InlineData(typeof(GenericA<string>), "G", typeof(object), typeof(GenericA<string>), "G", typeof(object))]
        [InlineData(typeof(GenericA<object>), "G", typeof(object), typeof(GenericA<string>), "G", typeof(object))]
        [InlineData(typeof(GenericA<string>), "G", typeof(object), typeof(GenericA<object>), "G", typeof(object))]
        [InlineData(typeof(GenericA<int>), "G", typeof(object), typeof(GenericA<int>), "G", typeof(object))]
        [InlineData(typeof(GenericA<int?>), "G", typeof(object), typeof(GenericA<int?>), "G", typeof(object))]
        [InlineData(typeof(GenericA<object>), "G", typeof(string), typeof(GenericA<object>), "G", typeof(string))]
        [InlineData(typeof(GenericA<string>), "G", typeof(string), typeof(GenericA<string>), "G", typeof(string))]
        [InlineData(typeof(GenericA<object>), "G", typeof(string), typeof(GenericA<string>), "G", typeof(string))]
        [InlineData(typeof(GenericA<string>), "G", typeof(string), typeof(GenericA<object>), "G", typeof(string))]
        [InlineData(typeof(GenericA<int>), "G", typeof(string), typeof(GenericA<int>), "G", typeof(string))]
        [InlineData(typeof(GenericA<int?>), "G", typeof(string), typeof(GenericA<int?>), "G", typeof(string))]
        [InlineData(typeof(GenericA<object>), "G", typeof(object), typeof(GenericA<object>), "G", typeof(string))]
        [InlineData(typeof(GenericA<string>), "G", typeof(object), typeof(GenericA<string>), "G", typeof(string))]
        [InlineData(typeof(GenericA<object>), "G", typeof(object), typeof(GenericA<string>), "G", typeof(string))]
        [InlineData(typeof(GenericA<string>), "G", typeof(object), typeof(GenericA<object>), "G", typeof(string))]
        [InlineData(typeof(GenericA<int>), "G", typeof(object), typeof(GenericA<int>), "G", typeof(string))]
        [InlineData(typeof(GenericA<int?>), "G", typeof(object), typeof(GenericA<int?>), "G", typeof(string))]
        [InlineData(typeof(GenericA<object>), "G", typeof(string), typeof(GenericA<object>), "G", typeof(object))]
        [InlineData(typeof(GenericA<string>), "G", typeof(string), typeof(GenericA<string>), "G", typeof(object))]
        [InlineData(typeof(GenericA<object>), "G", typeof(string), typeof(GenericA<string>), "G", typeof(object))]
        [InlineData(typeof(GenericA<string>), "G", typeof(string), typeof(GenericA<object>), "G", typeof(object))]
        [InlineData(typeof(GenericA<int>), "G", typeof(string), typeof(GenericA<int>), "G", typeof(object))]
        [InlineData(typeof(GenericA<int?>), "G", typeof(string), typeof(GenericA<int?>), "G", typeof(object))]
        [InlineData(typeof(GenericA<object>), "G", typeof(int), typeof(GenericA<object>), "G", typeof(int))]
        [InlineData(typeof(GenericA<string>), "G", typeof(int), typeof(GenericA<string>), "G", typeof(int))]
        [InlineData(typeof(GenericA<object>), "G", typeof(int), typeof(GenericA<string>), "G", typeof(int))]
        [InlineData(typeof(GenericA<string>), "G", typeof(int), typeof(GenericA<object>), "G", typeof(int))]
        [InlineData(typeof(GenericA<int>), "G", typeof(int), typeof(GenericA<int>), "G", typeof(int))]
        [InlineData(typeof(GenericA<int?>), "G", typeof(int), typeof(GenericA<int?>), "G", typeof(int))]
        [InlineData(typeof(GenericA<object>), "G", typeof(int?), typeof(GenericA<object>), "G", typeof(int?))]
        [InlineData(typeof(GenericA<string>), "G", typeof(int?), typeof(GenericA<string>), "G", typeof(int?))]
        [InlineData(typeof(GenericA<object>), "G", typeof(int?), typeof(GenericA<string>), "G", typeof(int?))]
        [InlineData(typeof(GenericA<string>), "G", typeof(int?), typeof(GenericA<object>), "G", typeof(int?))]
        [InlineData(typeof(GenericA<int>), "G", typeof(int?), typeof(GenericA<int>), "G", typeof(int?))]
        [InlineData(typeof(GenericA<int?>), "G", typeof(int?), typeof(GenericA<int?>), "G", typeof(int?))]
        [InlineData(typeof(ValueGenericA<object>), "NG", null, typeof(ValueGenericA<object>), "NG", null)]
        [InlineData(typeof(ValueGenericA<string>), "NG", null, typeof(ValueGenericA<string>), "NG", null)]
        [InlineData(typeof(ValueGenericA<object>), "NG", null, typeof(ValueGenericA<string>), "NG", null)]
        [InlineData(typeof(ValueGenericA<string>), "NG", null, typeof(ValueGenericA<object>), "NG", null)]
        [InlineData(typeof(ValueGenericA<int>), "NG", null, typeof(ValueGenericA<int>), "NG", null)]
        [InlineData(typeof(ValueGenericA<int?>), "NG", null, typeof(ValueGenericA<int?>), "NG", null)]
        [InlineData(typeof(ValueGenericA<object>), "G", null, typeof(ValueGenericA<object>), "G", null)]
        [InlineData(typeof(ValueGenericA<string>), "G", null, typeof(ValueGenericA<string>), "G", null)]
        [InlineData(typeof(ValueGenericA<object>), "G", null, typeof(ValueGenericA<string>), "G", null)]
        [InlineData(typeof(ValueGenericA<string>), "G", null, typeof(ValueGenericA<object>), "G", null)]
        [InlineData(typeof(ValueGenericA<int>), "G", null, typeof(ValueGenericA<int>), "G", null)]
        [InlineData(typeof(ValueGenericA<int?>), "G", null, typeof(ValueGenericA<int?>), "G", null)]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(object), typeof(ValueGenericA<object>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(object), typeof(ValueGenericA<string>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(object), typeof(ValueGenericA<string>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(object), typeof(ValueGenericA<object>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<int>), "G", typeof(object), typeof(ValueGenericA<int>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<int?>), "G", typeof(object), typeof(ValueGenericA<int?>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(string), typeof(ValueGenericA<object>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(string), typeof(ValueGenericA<string>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(string), typeof(ValueGenericA<string>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(string), typeof(ValueGenericA<object>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<int>), "G", typeof(string), typeof(ValueGenericA<int>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<int?>), "G", typeof(string), typeof(ValueGenericA<int?>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(object), typeof(ValueGenericA<object>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(object), typeof(ValueGenericA<string>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(object), typeof(ValueGenericA<string>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(object), typeof(ValueGenericA<object>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<int>), "G", typeof(object), typeof(ValueGenericA<int>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<int?>), "G", typeof(object), typeof(ValueGenericA<int?>), "G", typeof(string))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(string), typeof(ValueGenericA<object>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(string), typeof(ValueGenericA<string>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(string), typeof(ValueGenericA<string>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(string), typeof(ValueGenericA<object>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<int>), "G", typeof(string), typeof(ValueGenericA<int>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<int?>), "G", typeof(string), typeof(ValueGenericA<int?>), "G", typeof(object))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(int), typeof(ValueGenericA<object>), "G", typeof(int))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(int), typeof(ValueGenericA<string>), "G", typeof(int))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(int), typeof(ValueGenericA<string>), "G", typeof(int))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(int), typeof(ValueGenericA<object>), "G", typeof(int))]
        [InlineData(typeof(ValueGenericA<int>), "G", typeof(int), typeof(ValueGenericA<int>), "G", typeof(int))]
        [InlineData(typeof(ValueGenericA<int?>), "G", typeof(int), typeof(ValueGenericA<int?>), "G", typeof(int))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(int?), typeof(ValueGenericA<object>), "G", typeof(int?))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(int?), typeof(ValueGenericA<string>), "G", typeof(int?))]
        [InlineData(typeof(ValueGenericA<object>), "G", typeof(int?), typeof(ValueGenericA<string>), "G", typeof(int?))]
        [InlineData(typeof(ValueGenericA<string>), "G", typeof(int?), typeof(ValueGenericA<object>), "G", typeof(int?))]
        [InlineData(typeof(ValueGenericA<int>), "G", typeof(int?), typeof(ValueGenericA<int>), "G", typeof(int?))]
        [InlineData(typeof(ValueGenericA<int?>), "G", typeof(int?), typeof(ValueGenericA<int?>), "G", typeof(int?))]
        // TODO: add cases that mix GenericA and ValueGenericA
        public void MethodsCompareEqual(Type aDecl, string aName, Type? aParam, Type bDecl, string bName, Type? bParam)
        {
            MethodInfo Method(Type decl, string name, Type genParam)
            {
                var info = Helpers.ThrowIfNull(decl).GetMethods().First(m => m.Name == name);
                if (genParam != null)
                    info = info.MakeGenericMethod(genParam);
                return info;
            }

            MethodBase a = Method(aDecl, aName, aParam);
            MethodBase b = Method(bDecl, bName, bParam);

            var comparer = new GenericMethodInstantiationComparer();
            Assert.True(comparer.Equals(a, b));
            Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
        }


        private class GenericA<T>
        {
            public static void NG(T _) { }
            public static U G<U>(T _) => default;
        }
        private struct ValueGenericA<T>
        {
            public static void NG(T _) { }
            public static U G<U>(T _) => default;
        }
    }
}
