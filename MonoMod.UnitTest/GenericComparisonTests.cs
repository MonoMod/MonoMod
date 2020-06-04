using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MonoMod.UnitTest {
    public class GenericComparisonTests {

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
        public void TypesCompareEqual(Type a, Type b) {
            IEqualityComparer<Type> comparer = new GenericTypeInstantiationComparer();
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
        public void TypesCompareInequal(Type a, Type b) {
            IEqualityComparer<Type> comparer = new GenericTypeInstantiationComparer();
            Assert.False(comparer.Equals(a, b));
        }
        private class GenericA<T> { }
        private struct ValueGenericA<T> { }
    }
}
