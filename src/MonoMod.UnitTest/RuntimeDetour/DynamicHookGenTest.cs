#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using static MonoMod.RuntimeDetour.DynamicHookGen;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace MonoMod.UnitTest {
    public unsafe class DynamicHookGenTest {

        public static bool IsHook;

        // TODO: reenable when new RuntimeDetour has DynamicHookGen
        [Fact(Skip = "New RuntimeDetour doesn't have DynamicHookGen yet")]
        public void TestDynamicHookGen() {
            Assert.Equal(42, TestMethod(1, 2));


            On.MonoMod.UnitTest.DynamicHookGenTest.TestMethod += (Func<Func<int, int, int>, int, int, int>) TestMethodHook;
            Assert.Equal(42 + 1, TestMethod(1, 2));


            Func<Func<int, int, int>, int, int, int> hook = (orig, a, b) => {
                return orig(a, b) + b;
            };
            On.MonoMod.UnitTest.DynamicHookGenTest.TestMethod += hook;
            Assert.Equal(42 + 1 + 2, TestMethod(1, 2));


            ILContext.Manipulator manip = ctx => {
                ILCursor c = new ILCursor(ctx).GotoNext(i => i.MatchRet());
                c.Emit(OpCodes.Ldc_I4_3);
                c.Emit(OpCodes.Add);
            };
            IL.MonoMod.UnitTest.DynamicHookGenTest.TestMethod += manip;
            Assert.Equal(42 + 1 + 2 + 3, TestMethod(1, 2));


            On(typeof(DynamicHookGenTest)).TestMethod -= (Func<Func<int, int, int>, int, int, int>) TestMethodHook;
            Assert.Equal(42 + 2 + 3, TestMethod(1, 2));


            OnOrIL(typeof(DynamicHookGenTest)).TestMethod -= hook;
            Assert.Equal(42 + 3, TestMethod(1, 2));


            OnOrIL(typeof(DynamicHookGenTest)).TestMethod -= manip;
            Assert.Equal(42, TestMethod(1, 2));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TestMethod(int a, int b) {
            return 42;
        }

        public static int TestMethodHook(Func<int, int, int> orig, int a, int b) {
            return orig(a, b) + a;
        }

    }
}
