using MonoMod.Core;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Core
{
    public class DetouringInheritedMethods : TestBase
    {
        public DetouringInheritedMethods(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void GenericMethodInstantiation()
        {
            using (DetourFactory.Current.CreateDetour(
                typeof(Derived).GetMethod(nameof(Base.Name)).MakeGenericMethod(typeof(int)),
                typeof(DetouringInheritedMethods).GetMethod(nameof(Hooked), BindingFlags.Static | BindingFlags.NonPublic)))
            {
                Assert.Equal("Hooked", new Derived().Name<int>());
            }
        }

        [Fact]
        public void MethodsDifferingInCase()
        {
            using (DetourFactory.Current.CreateDetour(
                typeof(Derived).GetMethod(nameof(Base.Run)),
                typeof(DetouringInheritedMethods).GetMethod(nameof(Hooked), BindingFlags.Static | BindingFlags.NonPublic)))
            {
                Assert.Equal("Hooked", new Derived().Run());
                Assert.Equal("run", new Derived().run());
            }
        }

        private static string Hooked(Base self) => "Hooked";

        private class Base
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public string Name<T>() => typeof(T).Name;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public string Run() => "Run";

            [MethodImpl(MethodImplOptions.NoInlining)]
            public string run() => "run";
        }

        private class Derived : Base
        {
        }
    }
}
