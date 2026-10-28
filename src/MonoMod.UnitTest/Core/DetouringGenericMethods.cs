using MonoMod.Core;
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
    }
}
