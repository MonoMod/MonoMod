#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

#if NETFRAMEWORK

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Reflection.Emit;
using System.Text;
using Harmony;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class HarmonyBridgeTest {
        [Fact]
        public void TestHooks() {
            Assert.True(HarmonyDetourBridge.Init());

            Assert.Equal(42, TestMethod(1, 2));

            MethodInfo original = typeof(HarmonyBridgeTest).GetMethod("TestMethod");

            HarmonyInstance harmony = HarmonyInstance.Create("ga.0x0ade.monomod.test.bridge");
            harmony.Patch(original, null, new HarmonyMethod(typeof(HarmonyBridgeTest).GetMethod("TestPostfix")));
            Assert.Equal(42 + 1, TestMethod(1, 2));

            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) => {
                    return orig(a, b) + b;
                })
            )) {
                Assert.Equal(42 + 1 + 2, TestMethod(1, 2));
            }

            harmony.UnpatchAll();

            HarmonyDetourBridge.Reset();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TestMethod(int a, int b) {
            return 42;
        }

        public static int TestPostfix(int __result, int a, int b) {
            return __result + a;
        }

    }
}

#endif
