extern alias New;
using New::MonoMod.RuntimeDetour.Generics;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.RuntimeDetour.Generics
{
    [Collection("RuntimeDetour")]
    public class GenericHookOrigIdentityTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        private static GenericHook Hook(MethodInfo src, string targetName, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookOrigIdentityTest), targetName), vectors);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string DescribeWrap<T>(Func<T> orig) => $"wrap:{typeof(T).Name}:{orig()}";

        [GenericHookFact]
        public void OrigSeesCorrectTypeIdentityAcrossSharedInstantiations()
        {
            var src = GetMethod(typeof(OrigId), nameof(OrigId.Describe));
            Assert.True(IsShared(src, typeof(string)));
            Assert.True(IsShared(src, typeof(object)));

            using var hook = Hook(src, nameof(DescribeWrap), [typeof(string)], [typeof(object)]);

            Assert.Equal("wrap:String:String", OrigId.Describe<string>());
            Assert.Equal("wrap:Object:Object", OrigId.Describe<object>());
        }
    }

    public static class OrigId
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Describe<T>() => typeof(T).Name;
    }
}
