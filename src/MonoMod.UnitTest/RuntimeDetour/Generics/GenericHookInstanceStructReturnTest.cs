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
    public class GenericHookInstanceStructReturnTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        private static GenericHook Hook(MethodInfo src, string targetName, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookInstanceStructReturnTest), targetName), vectors);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Big MakeBigReplace<T>(InstanceBig self, T value) => new Big(self.Seed + 100, typeof(T).Name.Length);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Big MakeBigWrap<T>(Func<InstanceBig, T, Big> orig, InstanceBig self, T value)
        {
            var b = orig(self, value);
            return new Big(b.A + 1000, b.F);
        }

        [GenericHookFact]
        public void InstanceStructReturnSharedReplaced()
        {
            var src = GetMethod(typeof(InstanceBig), nameof(InstanceBig.Make));
            Assert.True(IsShared(src, typeof(string)));

            using var hook = Hook(src, nameof(MakeBigReplace), [typeof(string)]);

            var obj = new InstanceBig(5);
            Assert.Equal(new Big(105, 6), obj.Make<string>("x"));
        }

        [GenericHookFact]
        public void InstanceStructReturnSharedWithOrig()
        {
            var src = GetMethod(typeof(InstanceBig), nameof(InstanceBig.Make));
            Assert.True(IsShared(src, typeof(string)));

            using var hook = Hook(src, nameof(MakeBigWrap), [typeof(string)]);

            var obj = new InstanceBig(7);
            Assert.Equal(new Big(1007, 13), obj.Make<string>("x"));
        }
    }

    public class InstanceBig
    {
        public long Seed;
        public InstanceBig(long seed) => Seed = seed;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Big Make<T>(T value) => new Big(Seed, Seed + typeof(T).Name.Length);
    }
}
