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
    public class GenericHookArgRegisterPackingTest(ITestOutputHelper helper) : GenericTestBase(helper)
    {

        private static GenericHook Hook(MethodInfo src, string targetName, params Type[][] vectors) => new(src, GetMethod(typeof(GenericHookArgRegisterPackingTest), targetName), vectors);

        public struct Pair
        {
            public long A;
            public long B;
        }

        public static class PackSrc
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string StructThenGeneric<T>(Pair p, T value) => $"orig<{typeof(T).Name}>:{p.A},{p.B}:{value}";

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string StructThenGenericOrig<T>(Pair p, T value) => $"orig<{typeof(T).Name}>:{p.A},{p.B}:{value}";

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string FloatsThenGeneric<T>(double d1, double d2, T value) => $"orig<{typeof(T).Name}>:{d1},{d2}:{value}";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string StructThenGenericHook<T>(Pair p, T value) => $"hook<{typeof(T).Name}>:{p.A},{p.B}:{value}";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string StructThenGenericWrap<T>(Func<Pair, T, string> orig, Pair p, T value) => $"wrap[{orig(p, value)}]";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string FloatsThenGenericHook<T>(double d1, double d2, T value) => $"hook<{typeof(T).Name}>:{d1},{d2}:{value}";

        [GenericHookFact]
        public void TwoWordStructBeforeGenericArg()
        {
            var src = GetMethod(typeof(PackSrc), nameof(PackSrc.StructThenGeneric));
            using var hook = Hook(src, nameof(StructThenGenericHook), [typeof(string)]);

            var p = new Pair { A = 11, B = 22 };
            Assert.Equal("hook<String>:11,22:x", PackSrc.StructThenGeneric(p, "x"));
        }

        [GenericHookFact]
        public void TwoWordStructBeforeGenericArgWithOrig()
        {
            var src = GetMethod(typeof(PackSrc), nameof(PackSrc.StructThenGenericOrig));
            using var hook = Hook(src, nameof(StructThenGenericWrap), [typeof(string)]);

            var p = new Pair { A = 11, B = 22 };
            Assert.Equal("wrap[orig<String>:11,22:x]", PackSrc.StructThenGenericOrig(p, "x"));
        }

        [GenericHookFact]
        public void FloatsBeforeGenericArg()
        {
            var src = GetMethod(typeof(PackSrc), nameof(PackSrc.FloatsThenGeneric));
            using var hook = Hook(src, nameof(FloatsThenGenericHook), [typeof(string)]);

            Assert.Equal("hook<String>:1,2:x", PackSrc.FloatsThenGeneric(1.0, 2.0, "x"));
        }
    }
}
