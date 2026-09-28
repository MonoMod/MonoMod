extern alias New;
using New::MonoMod.RuntimeDetour.Generics;
using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.RuntimeDetour.Generics
{
    public class GenericSharingTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        #region TypeIsShared
        [Fact]
        public void TypeIsSharedPrimitiveIsNotShared()
        {
            Assert.False(GenericHelper.TypeIsShared(typeof(int)));
        }

        [Fact]
        public void TypeIsSharedNonGenericValueTypeIsNotShared()
        {
            Assert.False(GenericHelper.TypeIsShared(typeof(DateTime)));
        }

        [Fact]
        public void TypeIsSharedReferenceTypeIsShared()
        {
            Assert.True(GenericHelper.TypeIsShared(typeof(string)));
            Assert.True(GenericHelper.TypeIsShared(typeof(object)));
            Assert.True(GenericHelper.TypeIsShared(typeof(GenBase)));
        }

        [Fact]
        public void TypeIsSharedValueGenericWithReferenceArgIsShared()
        {
            Assert.True(GenericHelper.TypeIsShared(typeof(KeyValuePair<int, string>)));
        }

        [Fact]
        public void TypeIsSharedValueGenericWithAllValueArgsIsNotShared()
        {
            Assert.False(GenericHelper.TypeIsShared(typeof(KeyValuePair<int, long>)));
            Assert.False(GenericHelper.TypeIsShared(typeof(int?)));
        }

        [Fact]
        public void TypeIsSharedOpenGenericThrows()
        {
            Assert.Throws<InvalidOperationException>(() => GenericHelper.TypeIsShared(typeof(List<>)));
        }

        [Fact]
        public void TypeIsSharedNullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => GenericHelper.TypeIsShared(null!));
        }
        #endregion

        #region MethodIsShared
        [Fact]
        public void MethodIsSharedNonGenericMethodOnNonGenericTypeIsNotShared()
        {
            Assert.False(GenericHelper.MethodIsShared(GetMethod(typeof(object), nameof(ToString))));
        }

        [Fact]
        public void MethodIsSharedGenericMethodDependsOnMethodArg()
        {
            var open = GetMethod(typeof(Src), nameof(Src.GenericHookCtor));
            Assert.False(GenericHelper.MethodIsShared(open.MakeGenericMethod(typeof(int))));
            Assert.True(GenericHelper.MethodIsShared(open.MakeGenericMethod(typeof(string))));
        }

        [Fact]
        public void MethodIsSharedMethodOnGenericTypeDependsOnTypeArg()
        {
            Assert.False(GenericHelper.MethodIsShared(typeof(List<int>).GetMethod(nameof(List<int>.Add))!));
            Assert.True(GenericHelper.MethodIsShared(typeof(List<string>).GetMethod(nameof(List<string>.Add))!));

            Assert.False(GenericHelper.MethodIsShared(typeof(GcInst<int>).GetMethod(nameof(GcInst<int>.Describe))!));
            Assert.True(GenericHelper.MethodIsShared(typeof(GcInst<string>).GetMethod(nameof(GcInst<string>.Describe))!));
        }

        [Fact]
        public void MethodIsSharedTwoParamGenericMethodSharedIfAnyArgIsReference()
        {
            var open = GetMethod(typeof(SrcTwo), nameof(SrcTwo.SameValue));
            Assert.False(GenericHelper.MethodIsShared(open.MakeGenericMethod(typeof(int), typeof(long))));
            Assert.True(GenericHelper.MethodIsShared(open.MakeGenericMethod(typeof(int), typeof(string))));
            Assert.True(GenericHelper.MethodIsShared(open.MakeGenericMethod(typeof(string), typeof(int))));
        }

        [Fact]
        public void MethodIsSharedNullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => GenericHelper.MethodIsShared(null!));
        }
        #endregion

        #region InstantiationKey
        private static InstantiationKey Key(params Type[] args) => new(args);

        [Fact]
        public void InstantiationKeyEqualityByContent()
        {
            var a = Key(typeof(int), typeof(string));
            var b = Key(typeof(int), typeof(string));
            var c = Key(typeof(string), typeof(int));

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());

            Assert.False(a == c);
            Assert.True(a != c);
        }

        [Fact]
        public void InstantiationKeyEqualityLengthMismatch()
        {
            Assert.False(Key(typeof(int)).Equals(Key(typeof(int), typeof(string))));
        }

        [Fact]
        public void InstantiationKeyToString()
        {
            Assert.Equal("<Int32,String>", Key(typeof(int), typeof(string)).ToString());
        }

        [Fact]
        public void InstantiationKeyMatchesAssignableFrom()
        {
            Assert.True(InstantiationKey.Matches(Key(typeof(GenBase)), Key(typeof(GenBase))));
            Assert.True(InstantiationKey.Matches(Key(typeof(GenBase)), Key(typeof(GenDerived))));
            Assert.False(InstantiationKey.Matches(Key(typeof(GenBase)), Key(typeof(GenUnrelated))));
        }

        [Fact]
        public void InstantiationKeyMatchesLengthMismatch()
        {
            Assert.False(InstantiationKey.Matches(Key(typeof(GenBase)), Key(typeof(GenBase), typeof(int))));
        }

        [Fact]
        public void InstantiationKeyMatchesPerPosition()
        {
            Assert.True(InstantiationKey.Matches(Key(typeof(GenBase), typeof(int)), Key(typeof(GenDerived), typeof(int))));
            Assert.False(InstantiationKey.Matches(Key(typeof(GenBase), typeof(int)), Key(typeof(GenDerived), typeof(long))));
        }
        #endregion
    }
}
