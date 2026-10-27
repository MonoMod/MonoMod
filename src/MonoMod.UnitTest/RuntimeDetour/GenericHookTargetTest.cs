extern alias New;
using New::MonoMod.RuntimeDetour;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class GenericHookTargetTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void TestGenericTypeTarget()
        {
            using var hook = new Hook(
                typeof(GenericHookTargetTest).GetMethod("Source", BindingFlags.Static | BindingFlags.NonPublic)!,
                typeof(Targets<string>).GetMethod("Target", BindingFlags.Static | BindingFlags.NonPublic)!
            );

            Assert.Equal("String a", Source("a"));
        }

        [Fact]
        public void TestGenericTargetReturningStruct()
        {
            using var hook = new Hook(
                typeof(GenericHookTargetTest).GetMethod("StructSource", BindingFlags.Static | BindingFlags.NonPublic)!,
                typeof(GenericHookTargetTest).GetMethod("StructTarget", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(string))
            );

            Assert.Equal(("String", "a", "b"), StructSource("a"));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Source(string value) => value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static (string, string, string) StructSource(string value) => default;

        internal static (string, string, string) StructTarget<T>(string value) => (typeof(T).Name, value, "b");

        internal static class Targets<T>
        {
            internal static string Target(T value) => typeof(T).Name + " " + value;
        }
    }
}
