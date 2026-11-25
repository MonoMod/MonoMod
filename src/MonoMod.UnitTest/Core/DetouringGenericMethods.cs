using MonoMod.Core;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    public class DetouringGenericMethods : TestBase
    {
        public DetouringGenericMethods(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void GenericMethodInstantiation()
        {
            using (DetourFactory.Current.CreateDetour(
                typeof(DetouringGenericMethods).GetMethod(nameof(Identity), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(typeof(int)),
                typeof(DetouringGenericMethods).GetMethod(nameof(Two), BindingFlags.Static | BindingFlags.NonPublic)))
            {
                Assert.Equal(2, Identity(1));
                Assert.Equal(1L, Identity(1L));
            }

            Assert.Equal(1, Identity(1));
        }

        [Fact]
        public void StaticMethodOfSharedGenericType()
        {
            // detours on shared code don't take effect on every platform yet, so only applying and undoing them is checked
            using (DetourFactory.Current.CreateDetour(
                typeof(GenericType<string>).GetMethod(nameof(GenericType<string>.Name)),
                typeof(DetouringGenericMethods).GetMethod(nameof(Hooked), BindingFlags.Static | BindingFlags.NonPublic)))
            {
                GenericType<string>.Name();
            }

            Assert.Equal("String", GenericType<string>.Name());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T Identity<T>(T value) => value;

        private static int Two(int value) => 2;

        private static string Hooked() => "Hooked";

        private static class GenericType<T>
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string Name() => typeof(T).Name;
        }

        [SkipMonoAndOldCoreClrFact]
        public void ClosedGenericTypeInstantiation()
        {
            var originalMethod1 = typeof(AbstractGenericTestObject<double, DoubleTestItem>).GetMethod(nameof(AbstractGenericTestObject<,>.GetDouble));
            Assert.NotNull(originalMethod1);

            var replacement1 = typeof(DetouringGenericMethods).GetMethod(nameof(Detour1), BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(replacement1);

            var obj1 = new TestObjectDouble();
            var obj2 = new TestObjectString();
            Assert.Equal(1, obj1.GetDouble());
            Assert.Equal(1, obj2.GetDouble());

            using var detour1 = DetourFactory.Current.CreateDetour(originalMethod1, replacement1);

            Assert.Equal(999, obj1.GetDouble());
            Assert.Equal(1, obj2.GetDouble());

            var originalMethod2 = typeof(AbstractGenericTestObject<string, StringTestItem>).GetMethod(nameof(AbstractGenericTestObject<,>.GetDouble));
            Assert.NotNull(originalMethod2);

            var replacement2 = typeof(DetouringGenericMethods).GetMethod(nameof(Detour2), BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(replacement2);

            using var detour2 = DetourFactory.Current.CreateDetour(originalMethod2, replacement2);
            Assert.Equal(888, obj2.GetDouble());
            Assert.Equal(999, obj1.GetDouble());

            detour1.Undo();
            Assert.Equal(1, obj1.GetDouble());
            Assert.Equal(888, obj2.GetDouble());

            detour2.Undo();
            Assert.Equal(1, obj1.GetDouble());
            Assert.Equal(1, obj2.GetDouble());
        }

        [SkipMonoFact]
        public void OpenGenericTypeDefinitionThrows()
        {
            var originalMethod = typeof(TestSingleGenericObject<>).GetMethod(nameof(TestSingleGenericObject<>.GetDouble));
            Assert.NotNull(originalMethod);

            var replacement = typeof(DetouringGenericMethods).GetMethod(nameof(Detour3), BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(replacement);

            var instance = new TestSingleGenericObject<string>();
            Assert.Equal(1, instance.GetDouble());

            // Creating a detour on an open generic type definition (TestSingleGenericObject<>)
            // throws because methods on unbound generic types cannot be JIT compiled.
            // To detour a method on a generic type, use a closed generic type like TestSingleGenericObject<string>.
            Assert.Throws<ArgumentException>(() => DetourFactory.Current.CreateDetour(originalMethod, replacement));

            // Verify the instance still works normally
            Assert.Equal(1, instance.GetDouble());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double Detour1(object self) => 999;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double Detour2(object self) => 888;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double Detour3(object self) => 777;

        private abstract class AbstractGenericTestObject<T, TItem> where TItem : ITestItem
        {
            public virtual double GetDouble() => 1;
        }

        private sealed class TestObjectDouble : AbstractGenericTestObject<double, DoubleTestItem>;
        private sealed class TestObjectString : AbstractGenericTestObject<string, StringTestItem>;

        private sealed class TestSingleGenericObject<T>
        {
            public double GetDouble() => 1;
        }

        private sealed class DoubleTestItem : ITestItem;
        private sealed class StringTestItem : ITestItem;

        private interface ITestItem;
    }
}
