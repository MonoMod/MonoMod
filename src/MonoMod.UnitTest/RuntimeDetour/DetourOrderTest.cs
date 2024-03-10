#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class DetourOrderTest : TestBase
    {
        public DetourOrderTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDetoursOrder()
        {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            var original = typeof(DetourOrderTest).GetMethod("TestMethod");

            // Hooks behave as if they were "stacked" on top of each other.
            // In other words, the default "hook order" is reverse of the running order.
            // The expected list takes this into account.
            string[] order = {
                "Default",
                "AfterA",
                "A",
                "BeforeA",
            };
            string[] expected = {
                order[3],
                order[2],
                order[1],
                order[0], // "Default" can end up anywhere.
            };
            var actual = new List<string>();

            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) =>
                {
                    actual.Add(order[0]);
                    return orig(a, b) * a;
                })
            ))
            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) =>
                {
                    actual.Add(order[1]);
                    return orig(a, b) + b;
                }),
                new DetourConfig("AfterA").AddAfter("A")
            ))
            using (new DetourConfigContext(new("A")).Use())
            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) =>
                {
                    actual.Add(order[2]);
                    return orig(a, b) + a;
                })
            ))
            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) =>
                {
                    actual.Add(order[3]);
                    return orig(a, b) * b;
                }),
                new DetourConfig("BeforeA").AddBefore("A")
            ))
            {
                TestMethod(2, 3);

                var mdi = DetourManager.GetDetourInfo(original);
                using (mdi.WithLock())
                {
                    foreach (var d in mdi.Detours)
                    {
                        var config = d.Config;
                        Assert.True(d.IsApplied);
                    }
                    foreach (var i in mdi.ILHooks)
                    {
                        var config = i.Config;
                        Assert.True(i.IsApplied);
                    }
                }

                Assert.Equal(expected, actual);
            }

        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TestMethod(int a, int b)
        {
            return 2;
        }

    }
}
